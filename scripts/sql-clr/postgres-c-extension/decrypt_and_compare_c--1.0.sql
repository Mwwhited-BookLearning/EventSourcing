-- ADR-098 -- must be run via CREATE EXTENSION decrypt_and_compare_c, never
-- loaded directly by \i (see PostgreSQL's own extension-versioning rules).
\echo Use "CREATE EXTENSION decrypt_and_compare_c" to load this file. \quit

CREATE FUNCTION decrypt_and_compare_c(
    ciphertext_base64 TEXT,
    key_bytes BYTEA,
    data_type TEXT,
    comparison_operator TEXT,
    comparison_value TEXT
) RETURNS BOOLEAN
AS '$libdir/decrypt_and_compare_c', 'decrypt_and_compare_c'
LANGUAGE C IMMUTABLE STRICT PARALLEL SAFE;
