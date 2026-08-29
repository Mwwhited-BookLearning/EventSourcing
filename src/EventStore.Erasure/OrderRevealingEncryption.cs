using System.Globalization;
using System.Security.Cryptography;
using EventStore.Domain.SchemaRegistry;

namespace EventStore.Erasure;

// ADR-097 -- a from-scratch realization of the same high-level idea CLWW
// (Chenette/Lewi/Weis/Wu, FSE 2016) and Lewi-Wu (CCS 2016) popularized:
// per-prefix-keyed, block-level order-preserving encryption, revealing only
// the comparison result and how many leading blocks two values share --
// never the full numeric order the way plain OPE does. This is NOT a
// verified, byte-for-byte implementation of either paper's own formal
// construction -- built and correctness-tested this session, explicitly
// gated behind ADR-097's no-override registration refusal and its
// build-plan item's own required dedicated security review before any real
// use. Treat this as a working example of the mechanism and its real
// trade-offs, not an audited cryptographic library.
//
// Mechanism: encode the plaintext as a fixed-width, order-preserving 8-byte
// sequence (EncodeOrderPreserving), then encrypt block-by-block (1 byte per
// block, MSB first). Block i's own small order-preserving mapping (a keyed
// "sample enough random values, sort them, index by plaintext block value"
// table over {0..255}) is seeded from HMAC(key, i || plaintext-prefix-
// before-i) -- so two values sharing a plaintext prefix use the IDENTICAL
// per-block mapping up through their first differing block (where comparing
// ciphertext blocks directly is meaningful, since both sides derived the
// same sorted table), and completely unrelated mappings after it (where no
// further comparison is meaningful or attempted).
public static class OrderRevealingEncryption
{
    private const int BlockCount = 8; // one block per byte of the 8-byte order-preserving encoding
    private const int BlockDomainSize = 256; // one byte's value space

    public static byte[] Encrypt(byte[] key, string rawValueText, FilterableFieldType dataType)
    {
        var plaintextBlocks = EncodeOrderPreserving(rawValueText, dataType);
        var ciphertext = new byte[BlockCount * sizeof(ulong)];
        for (var i = 0; i < BlockCount; i++)
        {
            var seed = PrefixSeed(key, i, plaintextBlocks);
            var opeValue = OpeBlock(seed, plaintextBlocks[i]);
            WriteBlockBigEndian(ciphertext, i, opeValue);
        }
        return ciphertext;
    }

    // -1/0/1 -- meaningful ONLY when comparing two ciphertexts produced
    // under the SAME key for the same field (ADR-096's own per-(AppId,
    // EventTypeName, FieldJsonPath) key scoping already guarantees this at
    // the call site).
    public static int Compare(byte[] left, byte[] right)
    {
        for (var i = 0; i < BlockCount; i++)
        {
            var l = ReadBlockBigEndian(left, i);
            var r = ReadBlockBigEndian(right, i);
            if (l != r) return l < r ? -1 : 1;
        }
        return 0;
    }

    private static void WriteBlockBigEndian(byte[] ciphertext, int blockIndex, ulong value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        Array.Copy(bytes, 0, ciphertext, blockIndex * sizeof(ulong), sizeof(ulong));
    }

    private static ulong ReadBlockBigEndian(byte[] ciphertext, int blockIndex)
    {
        var bytes = new byte[sizeof(ulong)];
        Array.Copy(ciphertext, blockIndex * sizeof(ulong), bytes, 0, sizeof(ulong));
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private static byte[] PrefixSeed(byte[] key, int blockIndex, byte[] plaintextBlocks)
    {
        var input = new byte[1 + blockIndex];
        input[0] = (byte)blockIndex;
        Array.Copy(plaintextBlocks, 0, input, 1, blockIndex);
        return HMACSHA256.HashData(key, input);
    }

    // Deterministic "sample-then-sort" small-domain order-preserving map:
    // BlockDomainSize pseudorandom ulong samples, seeded by `seed`, sorted
    // ascending -- OpeBlock(seed, v) = the v-th smallest sample. Genuinely
    // order-preserving for a FIXED seed (samples are sorted); completely
    // unrelated for a different seed (a fresh random draw), which is what
    // limits leakage to same-prefix comparisons only.
    private static ulong OpeBlock(byte[] seed, byte value)
    {
        var samples = new ulong[BlockDomainSize];
        for (var i = 0; i < BlockDomainSize; i++)
            samples[i] = BitConverter.ToUInt64(HMACSHA256.HashData(seed, BitConverter.GetBytes(i)));
        Array.Sort(samples);
        return samples[value];
    }

    private static byte[] EncodeOrderPreserving(string rawValueText, FilterableFieldType dataType)
    {
        var encoded = dataType switch
        {
            FilterableFieldType.Number => DoubleToOrderPreservingULong(double.Parse(rawValueText, CultureInfo.InvariantCulture)),
            FilterableFieldType.DateTimeOffset => unchecked((ulong)DateTimeOffset.Parse(rawValueText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcTicks),
            _ => throw new NotSupportedException($"Order-Revealing Encryption only supports Number and DateTimeOffset fields (got: {dataType})"),
        };
        var bytes = BitConverter.GetBytes(encoded);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return bytes; // 8 big-endian bytes, one per block
    }

    // Standard bit-flip trick so a double's bit pattern, read as an unsigned
    // integer, sorts in the same order as the double's own numeric value:
    // flip the sign bit for a non-negative value, flip every bit for a
    // negative one.
    private static ulong DoubleToOrderPreservingULong(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var ubits = unchecked((ulong)bits);
        var mask = (ubits & 0x8000000000000000UL) != 0 ? 0xFFFFFFFFFFFFFFFFUL : 0x8000000000000000UL;
        return ubits ^ mask;
    }
}
