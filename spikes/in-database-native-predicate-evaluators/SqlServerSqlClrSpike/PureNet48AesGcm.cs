using System;
using System.Security.Cryptography;

namespace EventStore.SqlClr.SqlServer
{
    // Direct request, 2026-09-04: build the SQLCLR side with pure net48,
    // no NuGet/NET-Standard extension packages at all. Root cause this
    // replaces: Microsoft.Bcl.Cryptography's own transitive dependency
    // chain (System.Buffers/System.Memory/System.Numerics.Vectors/System.
    // Runtime.CompilerServices.Unsafe) fails SQL Server's CLR verifier
    // under PERMISSION_SET = SAFE in every available version -- confirmed
    // this session, not a version-pinning fix (see docs/adrs/adr-098-*.md's
    // own 2026-09-04 additive note for the full investigation). .NET
    // Framework 4.8 has no built-in AesGcm class (added in .NET Standard
    // 2.1/.NET Core 3.0, never backported) -- this is a from-scratch
    // AES-256-GCM (NIST SP 800-38D) implementation built ONLY on
    // System.Security.Cryptography.Aes's own ECB single-block primitive,
    // which IS in the classic net48 BCL, no package needed at all.
    //
    // Honest scope statement, matching OrderRevealingEncryption.cs's own
    // convention for exactly this kind of thing: this is a from-scratch
    // realization of the standard, publicly specified GCM construction,
    // not a byte-for-byte port of any existing library's source. Its own
    // correctness is verified against REAL ciphertext produced by .NET's
    // own System.Security.Cryptography.AesGcm (EnvelopeAesGcm.Encrypt
    // under net10.0, the actual production encryption path) -- successful
    // decryption of real, independently-produced ciphertext (not just
    // internal encrypt-then-decrypt self-consistency) is strong evidence
    // the construction is correct, per NIST SP 800-38D, but this has NOT
    // had a dedicated cryptographic security review the way ADR-097's ORE
    // construction explicitly named as still-needed before production use
    // -- the same recommendation applies here, for the same reason (a
    // hand-implemented cryptographic primitive, however carefully built
    // and empirically verified, is not a substitute for one).
    internal static class PureNet48AesGcm
    {
        private const int BlockSize = 16;

        // NIST SP 800-38D's own reduction constant for GF(2^128)
        // multiplication under this spec's bit-reflected convention: the
        // byte 0xE1 (0b11100001) in the top byte, zero elsewhere.
        private const byte RTopByte = 0xE1;

        public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag)
        {
            if (key.Length != 32) throw new ArgumentException("key must be 32 bytes (AES-256)", nameof(key));
            if (nonce.Length != 12) throw new ArgumentException("nonce must be 12 bytes (96-bit IV -- the only case this implementation supports, matching AesGcm's own default and EnvelopeAesGcm's own wire format)", nameof(nonce));
            if (tag.Length != 16) throw new ArgumentException("tag must be 16 bytes", nameof(tag));

            // AesManaged, not Aes.Create() -- found by actually deploying
            // to a real SQL Server: Aes.Create() returns a CAPI-backed
            // AesCryptoServiceProvider under classic .NET Framework, which
            // carries a [HostProtection(Synchronization = true)] attribute
            // (it wraps a native OS crypto handle) that SQL Server's CLR
            // host explicitly forbids even under PERMISSION_SET = SAFE --
            // a real System.Security.HostProtectionException, not a
            // verification failure (a different SQLCLR restriction class
            // than the one that blocked Microsoft.Bcl.Cryptography's own
            // dependency chain). AesManaged is a fully-managed
            // implementation with no native handle and no such
            // restriction.
            using (var aes = new AesManaged())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                using (var encryptor = aes.CreateEncryptor())
                {
                    // H = CIPH_K(0^128) -- the GHASH subkey, per spec 6.3/6.4.
                    var zeroBlock = new byte[BlockSize];
                    var h = EncryptBlock(encryptor, zeroBlock);

                    // J0 = IV || 0^31 || 1, the 96-bit-IV case (SP 800-38D
                    // 7.1, step 1) -- the only case EnvelopeAesGcm ever
                    // produces (its own fixed 12-byte nonce).
                    var j0 = new byte[BlockSize];
                    Array.Copy(nonce, j0, 12);
                    j0[15] = 1;

                    // P = GCTR_K(inc32(J0), C) -- decrypt starts the
                    // counter at J0+1, never J0 itself (that block is
                    // reserved for the tag computation below).
                    var counter = Increment32(j0);
                    var plaintext = Gctr(encryptor, counter, ciphertext);

                    // S = GHASH_H(A || 0^v || C || 0^u || [len(A)]_64 ||
                    // [len(C)]_64) -- A (additional authenticated data) is
                    // always empty for EnvelopeAesGcm's own construction,
                    // confirmed against EventStore.Erasure.EnvelopeAesGcm's
                    // own real Encrypt/Decrypt (no AAD parameter exists).
                    var s = GHash(h, Array.Empty<byte>(), ciphertext);

                    // T = MSB_t(GCTR_K(J0, S)) -- tag = encrypting S with
                    // the counter block set back to J0 itself (not J0+1).
                    var computedTag = Gctr(encryptor, j0, s);

                    if (!ConstantTimeEquals(computedTag, tag))
                        throw new CryptographicException("AES-GCM authentication tag mismatch -- wrong key or corrupted/tampered ciphertext.");

                    return plaintext;
                }
            }
        }

        private static byte[] EncryptBlock(ICryptoTransform encryptor, byte[] block)
        {
            var output = new byte[BlockSize];
            encryptor.TransformBlock(block, 0, BlockSize, output, 0);
            return output;
        }

        // GCTR_K(ICB, X): CB_1 = ICB, CB_i = inc32(CB_(i-1)); Y_i = X_i XOR
        // CIPH_K(CB_i) for each (possibly partial, for the final block)
        // 128-bit chunk of X. Symmetric -- used for both "encrypt the
        // counter stream to build keystream, XOR with ciphertext to
        // recover plaintext" (main decrypt) and "encrypt S to get the tag"
        // (single-block case, X = S).
        private static byte[] Gctr(ICryptoTransform encryptor, byte[] icb, byte[] x)
        {
            if (x.Length == 0) return Array.Empty<byte>();

            var y = new byte[x.Length];
            var counter = (byte[])icb.Clone();
            var offset = 0;
            while (offset < x.Length)
            {
                var keystreamBlock = EncryptBlock(encryptor, counter);
                var chunkLen = Math.Min(BlockSize, x.Length - offset);
                for (var i = 0; i < chunkLen; i++)
                    y[offset + i] = (byte)(x[offset + i] ^ keystreamBlock[i]);
                offset += chunkLen;
                counter = Increment32(counter);
            }
            return y;
        }

        // inc32: increments only the low-order 32 bits of a 128-bit block,
        // treated as a big-endian unsigned integer, wrapping on overflow --
        // the high-order 96 bits (the nonce, for J0's own descendants) are
        // never touched, per SP 800-38D 6.2.
        private static byte[] Increment32(byte[] block)
        {
            var result = (byte[])block.Clone();
            for (var i = 15; i >= 12; i--)
            {
                result[i]++;
                if (result[i] != 0) break; // no carry needed
            }
            return result;
        }

        // GHASH_H(X): Y_0 = 0; Y_i = (Y_(i-1) XOR X_i) . H in GF(2^128) for
        // each 128-bit block X_i of X (zero-padded to a full block if the
        // final chunk is short); result is Y_m after the length block.
        // Here X is built directly as A||pad||C||pad||lengths rather than
        // materializing the padded concatenation, since A is always empty.
        private static byte[] GHash(byte[] h, byte[] aad, byte[] ciphertext)
        {
            var y = new byte[BlockSize];

            foreach (var block in BlocksOf(aad))
                y = GfMultiply(Xor(y, block), h);
            foreach (var block in BlocksOf(ciphertext))
                y = GfMultiply(Xor(y, block), h);

            var lengthsBlock = new byte[BlockSize];
            WriteBigEndianUInt64(lengthsBlock, 0, (ulong)aad.Length * 8);
            WriteBigEndianUInt64(lengthsBlock, 8, (ulong)ciphertext.Length * 8);
            y = GfMultiply(Xor(y, lengthsBlock), h);

            return y;
        }

        private static System.Collections.Generic.IEnumerable<byte[]> BlocksOf(byte[] data)
        {
            for (var offset = 0; offset < data.Length; offset += BlockSize)
            {
                var block = new byte[BlockSize];
                var len = Math.Min(BlockSize, data.Length - offset);
                Array.Copy(data, offset, block, 0, len); // zero-padded, per spec
                yield return block;
            }
        }

        // The standard bit-reflected GF(2^128) multiplication algorithm
        // NIST SP 800-38D's own Algorithm 1 describes: processes X's 128
        // bits MSB-first (bit 0 = MSB of byte 0), right-shifting V by one
        // bit each round and conditionally XOR-ing the reduction constant
        // R (0xE1 in the top byte) whenever the bit shifted OUT of V's low
        // end was 1 -- this is the well-established reference algorithm
        // every real GCM implementation (OpenSSL, BoringSSL, etc.) computes
        // the identical result from, verified here empirically against
        // real .NET-produced ciphertext rather than merely by transcription
        // (see this class's own header comment on how that verification
        // was actually done).
        private static byte[] GfMultiply(byte[] x, byte[] y)
        {
            var z = new byte[BlockSize];
            var v = (byte[])y.Clone();

            for (var i = 0; i < 128; i++)
            {
                var byteIndex = i / 8;
                var bitMask = (byte)(0x80 >> (i % 8));
                if ((x[byteIndex] & bitMask) != 0)
                    Xor(z, v, z);

                var lsbSet = (v[15] & 1) != 0;
                ShiftRightOneBit(v);
                if (lsbSet)
                    v[0] ^= RTopByte;
            }
            return z;
        }

        private static void ShiftRightOneBit(byte[] block)
        {
            byte carry = 0;
            for (var i = 0; i < BlockSize; i++)
            {
                var nextCarry = (byte)(block[i] & 1);
                block[i] = (byte)((block[i] >> 1) | (carry << 7));
                carry = nextCarry;
            }
        }

        private static byte[] Xor(byte[] a, byte[] b)
        {
            var result = new byte[a.Length];
            Xor(a, b, result);
            return result;
        }

        private static void Xor(byte[] a, byte[] b, byte[] destination)
        {
            for (var i = 0; i < a.Length; i++)
                destination[i] = (byte)(a[i] ^ b[i]);
        }

        private static void WriteBigEndianUInt64(byte[] buffer, int offset, ulong value)
        {
            for (var i = 0; i < 8; i++)
                buffer[offset + i] = (byte)(value >> (8 * (7 - i)));
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
