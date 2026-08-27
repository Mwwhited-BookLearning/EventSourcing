-- ADR-098 -- the PostgreSQL half of the in-database native predicate
-- evaluator seam. pgcrypto's raw encrypt()/decrypt() functions support
-- only CBC/ECB (verified against the current PostgreSQL docs this
-- session) -- no GCM/AEAD mode at all, confirming ADR-098's own stated
-- gap. Genuine AES-256-GCM decrypt inside PostgreSQL, matching
-- EnvelopeAesGcm's exact wire format, needs a real cipher implementation
-- pgcrypto doesn't offer -- this uses plpython3u (an untrusted procedural
-- language: superuser-only to install, deliberately, since it grants
-- arbitrary code execution) and Python's well-established `cryptography`
-- package, rather than hand-rolling AES-GCM in PL/pgSQL, which has no
-- practical way to implement Galois-field multiplication.
--
-- Scope, same as the SQL Server side (ADR-098): only ever decrypts
-- ciphertext produced by the "Local" IErasureKeyStore/ISearchIndexKeyStore
-- backend -- the calling query supplies the raw key bytes itself (read
-- from local_erasure_key_materials/local_search_index_key_materials,
-- ordinary tables in the SAME database), so this function never needs
-- network access to a real KMS/Vault.
--
-- NOT verified against a running PostgreSQL instance this session --
-- unlike the SQL Server side (cross-checked against real
-- EnvelopeAesGcm-produced ciphertext under the actual net48 runtime SQL
-- Server's CLR host uses), neither plpython3u nor the `cryptography`
-- package is present in the standard Testcontainers postgres image this
-- project's own integration tests already use, and installing both would
-- mean building and maintaining a custom Postgres image -- a real,
-- separate piece of infrastructure work, named here rather than silently
-- skipped. Verify this function manually against a real golden
-- ciphertext (docs/changes/2026-08-27.md has one) before relying on it.

CREATE EXTENSION IF NOT EXISTS plpython3u;

CREATE OR REPLACE FUNCTION decrypt_and_compare(
    ciphertext_base64 TEXT,
    key_bytes BYTEA,
    data_type TEXT,             -- 'Number' | 'DateTimeOffset' | 'String'
    comparison_operator TEXT,   -- 'gt' | 'gte' | 'lt' | 'lte'
    comparison_value TEXT
) RETURNS BOOLEAN AS $$
    import base64
    from datetime import datetime
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM

    blob = base64.b64decode(ciphertext_base64)
    nonce, tag, ciphertext = blob[:12], blob[12:28], blob[28:]

    try:
        # EnvelopeAesGcm's wire format is nonce || tag || ciphertext;
        # Python's AESGCM.decrypt expects ciphertext || tag concatenated
        # (RFC 5116 order), so the tag moves to the end here.
        aesgcm = AESGCM(bytes(key_bytes))
        plaintext_bytes = aesgcm.decrypt(nonce, ciphertext + tag, None)
    except Exception:
        # A row that can't be decrypted under the supplied key (destroyed
        # key, corrupt row, wrong key from the calling query) can never
        # satisfy a comparison -- False, not an error that aborts the
        # whole query for one bad row (same posture as the SQL Server side).
        return False

    # PayloadEncryptor.EncryptLeafAsync encrypts the leaf's canonical JSON
    # text (e.g. `"42.5"`) -- always double-quoted, per that class's own
    # comment; strip the surrounding quotes to recover the plain value.
    plaintext = plaintext_bytes.decode("utf-8").strip('"')

    if data_type == "Number":
        comparison = (float(plaintext) > float(comparison_value)) - (float(plaintext) < float(comparison_value))
    elif data_type == "DateTimeOffset":
        a = datetime.fromisoformat(plaintext.replace("Z", "+00:00"))
        b = datetime.fromisoformat(comparison_value.replace("Z", "+00:00"))
        comparison = (a > b) - (a < b)
    else:
        comparison = (plaintext > comparison_value) - (plaintext < comparison_value)

    if comparison_operator == "gt":
        return comparison > 0
    elif comparison_operator == "gte":
        return comparison >= 0
    elif comparison_operator == "lt":
        return comparison < 0
    elif comparison_operator == "lte":
        return comparison <= 0
    else:
        raise ValueError("comparison_operator must be one of gt/gte/lt/lte")
$$ LANGUAGE plpython3u IMMUTABLE;

-- Example query shape (ADR-096's own bucket-narrowing already ran; this
-- is the exact-match step ADR-098 exists for, over an already-small
-- candidate set -- never a full-table scan):
--
-- SELECT e."SequenceNumber"
-- FROM "Events" e
-- JOIN "EncryptedFieldIndexEntries" idx ON idx."EntityId" = e."EntityId" AND idx."FieldJsonPath" = $1
-- JOIN "EntityErasureKeys" k ON k."EntityId" = e."EntityId"
-- JOIN "LocalErasureKeyMaterials" m ON m."KeyReference" = k."KeyReference"
-- WHERE e."SequenceNumber" = ANY($2)
--   AND decrypt_and_compare(e."Payload"::jsonb #>> $3, m."WrappedKey", $4, $5, $6);
