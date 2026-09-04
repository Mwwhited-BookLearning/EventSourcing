/*
 * ADR-098 -- a genuinely native (compiled C, PGXS-built) alternative to the
 * existing plpython3u decrypt_and_compare function, built to get real,
 * measured performance numbers rather than assume C is faster (direct
 * request: "let's try these extensions out to see how they perform").
 *
 * Mirrors EncryptedPredicateFunctions.DecryptAndCompareCore (SQL Server
 * SQLCLR side) and scripts/sql-clr/deploy-postgres-encrypted-predicate-
 * function.sql's own plpython3u function EXACTLY -- same wire format
 * (EnvelopeAesGcm: nonce[12] || tag[16] || ciphertext), same plaintext
 * convention (canonical JSON text, double-quoted, e.g. `"42.5"`), same
 * comparison semantics (gt/gte/lt/lte only -- eq/neq are handled by the
 * blind-index HMAC path elsewhere, never by any decrypt_and_compare
 * variant), same "can't decrypt -> false, never an aborting error" posture
 * for a bad key/corrupt row.
 *
 * Uses OpenSSL's EVP API directly for real AES-256-GCM -- pgcrypto's own
 * pgcrypto/pgp_sym_decrypt only supports CBC/ECB, confirmed against
 * PostgreSQL's own docs while building the plpython3u version (ADR-098's
 * own stated gap). PostgreSQL itself is typically already linked against
 * OpenSSL for SSL support, so libssl/libcrypto is a safe, standard link
 * dependency here, not a new one this extension introduces.
 */
#include "postgres.h"
#include "fmgr.h"
#include "utils/builtins.h"
#include "utils/timestamp.h"
#include "varatt.h"

#include <openssl/evp.h>
#include <openssl/err.h>
#include <string.h>
#include <stdlib.h>
#include <time.h>

PG_MODULE_MAGIC;

#define NONCE_SIZE 12
#define TAG_SIZE 16

/* Decrypts EnvelopeAesGcm-format ciphertext (nonce || tag || ciphertext)
 * with the given 32-byte key. Returns a newly-palloc'd, NUL-terminated
 * plaintext string on success, or NULL if decryption/authentication
 * fails (wrong key, corrupt ciphertext) -- caller treats NULL as "this
 * row can never satisfy the comparison," never as an error to propagate. */
static char *
decrypt_envelope_aes_gcm(const unsigned char *blob, int blob_len, const unsigned char *key, int key_len)
{
    if (blob_len < NONCE_SIZE + TAG_SIZE || key_len != 32)
        return NULL;

    const unsigned char *nonce = blob;
    const unsigned char *tag = blob + NONCE_SIZE;
    const unsigned char *ciphertext = blob + NONCE_SIZE + TAG_SIZE;
    int ciphertext_len = blob_len - NONCE_SIZE - TAG_SIZE;

    EVP_CIPHER_CTX *ctx = EVP_CIPHER_CTX_new();
    if (ctx == NULL)
        return NULL;

    unsigned char *plaintext = (unsigned char *) palloc(ciphertext_len + 1);
    int len = 0, plaintext_len = 0;
    bool ok = true;

    if (ok && EVP_DecryptInit_ex(ctx, EVP_aes_256_gcm(), NULL, NULL, NULL) != 1)
        ok = false;
    if (ok && EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_GCM_SET_IVLEN, NONCE_SIZE, NULL) != 1)
        ok = false;
    if (ok && EVP_DecryptInit_ex(ctx, NULL, NULL, key, nonce) != 1)
        ok = false;
    if (ok && EVP_DecryptUpdate(ctx, plaintext, &len, ciphertext, ciphertext_len) != 1)
        ok = false;
    plaintext_len = len;
    if (ok && EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_GCM_SET_TAG, TAG_SIZE, (void *) tag) != 1)
        ok = false;
    /* EVP_DecryptFinal_ex is where GCM tag verification actually happens --
     * a wrong key or tampered ciphertext fails HERE, not at DecryptUpdate,
     * matching AES-GCM's authenticated-encryption design (ciphertext is
     * only released as "real" plaintext once the tag verifies). */
    if (ok && EVP_DecryptFinal_ex(ctx, plaintext + len, &len) != 1)
        ok = false;
    plaintext_len += len;

    EVP_CIPHER_CTX_free(ctx);
    ERR_clear_error();

    if (!ok)
    {
        pfree(plaintext);
        return NULL;
    }

    plaintext[plaintext_len] = '\0';
    return (char *) plaintext;
}

/* PayloadEncryptor.EncryptLeafAsync encrypts the leaf's canonical JSON text
 * (e.g. `"42.5"`) -- always double-quoted. Strips the surrounding quotes
 * in place, matching both sibling implementations' own identical step. */
static char *
strip_json_quotes(char *s)
{
    size_t len = strlen(s);
    if (len >= 2 && s[0] == '"' && s[len - 1] == '"')
    {
        s[len - 1] = '\0';
        return s + 1;
    }
    return s;
}

/* Parses a fixed-shape UTC ISO-8601 timestamp (YYYY-MM-DDTHH:MM:SS[.fff]Z,
 * the exact shape this design's own DateTimeOffset values always take --
 * see PublishService/PayloadEncryptor) into seconds-since-epoch (+ a
 * fractional-second remainder, compared separately) for a genuine
 * chronological comparison, not a text comparison -- deliberately not
 * reusing the ORE work's "hex string order == byte order" trick here,
 * since arbitrary-precision fractional seconds and non-'Z' offsets are
 * NOT guaranteed shaped the same way payload data always is. */
static bool
parse_iso8601_utc(const char *s, time_t *out_secs, long *out_nanos)
{
    struct tm tm_val;
    memset(&tm_val, 0, sizeof(tm_val));
    int frac_digits_read = 0;
    long frac = 0;

    int n = sscanf(s, "%4d-%2d-%2dT%2d:%2d:%2d",
        &tm_val.tm_year, &tm_val.tm_mon, &tm_val.tm_mday,
        &tm_val.tm_hour, &tm_val.tm_min, &tm_val.tm_sec);
    if (n != 6)
        return false;
    tm_val.tm_year -= 1900;
    tm_val.tm_mon -= 1;

    const char *dot = strchr(s, '.');
    if (dot != NULL && dot < s + strlen(s))
    {
        const char *p = dot + 1;
        char digits[10] = {0};
        int i = 0;
        while (*p >= '0' && *p <= '9' && i < 9)
            digits[i++] = *p++;
        frac_digits_read = i;
        for (int pad = i; pad < 9; pad++)
            digits[pad] = '0';
        digits[9] = '\0';
        frac = atol(digits);
    }
    (void) frac_digits_read;

    *out_secs = timegm(&tm_val);
    *out_nanos = frac;
    return *out_secs != (time_t) -1;
}

static int
compare_datetimeoffset(const char *a, const char *b)
{
    time_t a_secs, b_secs;
    long a_nanos, b_nanos;
    if (!parse_iso8601_utc(a, &a_secs, &a_nanos) || !parse_iso8601_utc(b, &b_secs, &b_nanos))
        ereport(ERROR, (errmsg("decrypt_and_compare_c: could not parse DateTimeOffset value")));

    if (a_secs != b_secs)
        return a_secs < b_secs ? -1 : 1;
    if (a_nanos != b_nanos)
        return a_nanos < b_nanos ? -1 : 1;
    return 0;
}

/* OpenSSL's own base64 decoder, not a Postgres internal (pg_base64_decode
 * turned out not to be part of the stable, directly-callable C API in
 * PG 18 -- found by actually trying to link against it, not assumed).
 * EVP_DecodeBlock decodes full 4-char groups including '=' padding as
 * zero bytes, so the real output length is (raw decoded length minus
 * however many '=' padding characters the input actually had) -- a
 * well-known, standard adjustment this function makes explicitly. */
static int
base64_decode(const char *in, int in_len, unsigned char *out)
{
    int decoded_len = EVP_DecodeBlock(out, (const unsigned char *) in, in_len);
    if (decoded_len < 0)
        return -1;
    int padding = 0;
    if (in_len >= 1 && in[in_len - 1] == '=') padding++;
    if (in_len >= 2 && in[in_len - 2] == '=') padding++;
    return decoded_len - padding;
}

PG_FUNCTION_INFO_V1(decrypt_and_compare_c);

Datum
decrypt_and_compare_c(PG_FUNCTION_ARGS)
{
    if (PG_ARGISNULL(0) || PG_ARGISNULL(1) || PG_ARGISNULL(2) || PG_ARGISNULL(3) || PG_ARGISNULL(4))
        PG_RETURN_NULL();

    text *ciphertext_b64_text = PG_GETARG_TEXT_PP(0);
    bytea *key_bytea = PG_GETARG_BYTEA_PP(1);
    text *data_type_text = PG_GETARG_TEXT_PP(2);
    text *comparison_operator_text = PG_GETARG_TEXT_PP(3);
    text *comparison_value_text = PG_GETARG_TEXT_PP(4);

    char *ciphertext_b64 = text_to_cstring(ciphertext_b64_text);
    char *data_type = text_to_cstring(data_type_text);
    char *comparison_operator = text_to_cstring(comparison_operator_text);
    char *comparison_value = text_to_cstring(comparison_value_text);

    int ciphertext_b64_len = strlen(ciphertext_b64);
    unsigned char *blob = (unsigned char *) palloc(((ciphertext_b64_len / 4) + 1) * 3);
    int blob_len = base64_decode(ciphertext_b64, ciphertext_b64_len, blob);
    if (blob_len < 0)
        PG_RETURN_BOOL(false); /* malformed base64 -- never satisfies, never errors */

    unsigned char *key = (unsigned char *) VARDATA_ANY(key_bytea);
    int key_len = VARSIZE_ANY_EXHDR(key_bytea);

    char *plaintext_raw = decrypt_envelope_aes_gcm(blob, blob_len, key, key_len);
    if (plaintext_raw == NULL)
        PG_RETURN_BOOL(false); /* wrong key / corrupt row -- never satisfies, never errors */

    char *plaintext = strip_json_quotes(plaintext_raw);

    int comparison;
    if (strcmp(data_type, "Number") == 0)
    {
        double a = strtod(plaintext, NULL);
        double b = strtod(comparison_value, NULL);
        comparison = (a > b) - (a < b);
    }
    else if (strcmp(data_type, "DateTimeOffset") == 0)
    {
        comparison = compare_datetimeoffset(plaintext, comparison_value);
    }
    else
    {
        comparison = strcmp(plaintext, comparison_value);
        if (comparison < 0) comparison = -1;
        if (comparison > 0) comparison = 1;
    }

    pfree(plaintext_raw);

    bool result;
    if (strcmp(comparison_operator, "gt") == 0) result = comparison > 0;
    else if (strcmp(comparison_operator, "gte") == 0) result = comparison >= 0;
    else if (strcmp(comparison_operator, "lt") == 0) result = comparison < 0;
    else if (strcmp(comparison_operator, "lte") == 0) result = comparison <= 0;
    else ereport(ERROR, (errmsg("decrypt_and_compare_c: comparison_operator must be one of gt/gte/lt/lte, got \"%s\"", comparison_operator)));

    PG_RETURN_BOOL(result);
}
