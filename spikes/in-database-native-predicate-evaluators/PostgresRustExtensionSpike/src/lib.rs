//! ADR-098 -- a genuinely native (compiled, `pgrx`-built) Rust alternative
//! to `plpython3u`'s `decrypt_and_compare`, built to compare against the
//! C/PGXS extension's own real, measured performance numbers rather than
//! assume one language's advantage over another (direct request: "Create a
//! rust version for Postgres", following the earlier "let's try these
//! extensions out to see how they perform" benchmark).
//!
//! Uses `pgrx` (the underlying Rust-extension framework), not `plrust`
//! (the *trusted-language* wrapper built on top of it) -- `plrust`'s own
//! release is pinned to `pgrx = "=0.11.0"`, which only declares support up
//! through Postgres 16, a hard compile-time wall against this project's
//! real `postgres:18` target (verified this session, not assumed). `pgrx`
//! itself is actively maintained (v0.19.2, current) and genuinely supports
//! Postgres 18 -- confirmed against its own Cargo.toml feature list and
//! README before starting this build, not assumed by association with the
//! stale `plrust` product built on an old version of it.
//!
//! Mirrors EncryptedPredicateFunctions.DecryptAndCompareCore (SQL Server
//! SQLCLR), decrypt_and_compare_c (the PostgreSQL C/PGXS extension), and
//! scripts/sql-clr/deploy-postgres-encrypted-predicate-function.sql
//! (plpython3u) EXACTLY -- same EnvelopeAesGcm wire format (nonce[12] ||
//! tag[16] || ciphertext), same canonical-JSON-quoted plaintext
//! convention, same gt/gte/lt/lte-only comparison set (eq/neq are the
//! blind-index HMAC path's job elsewhere, never this function's), same
//! "can't decrypt -> false, never an aborting error" posture.
use aes_gcm::aead::{Aead, KeyInit};
use aes_gcm::{Aes256Gcm, Key, Nonce};
use pgrx::prelude::*;

::pgrx::pg_module_magic!();

const NONCE_SIZE: usize = 12;
const TAG_SIZE: usize = 16;

/// Decrypts EnvelopeAesGcm-format ciphertext (nonce || tag || ciphertext)
/// with the given 32-byte key. `None` on any failure (wrong key, corrupt
/// ciphertext, malformed input) -- caller treats that as "this row can
/// never satisfy the comparison," never propagates it as an error.
fn decrypt_envelope_aes_gcm(blob: &[u8], key_bytes: &[u8]) -> Option<Vec<u8>> {
    if blob.len() < NONCE_SIZE + TAG_SIZE || key_bytes.len() != 32 {
        return None;
    }
    let nonce_bytes = &blob[..NONCE_SIZE];
    let tag = &blob[NONCE_SIZE..NONCE_SIZE + TAG_SIZE];
    let ciphertext = &blob[NONCE_SIZE + TAG_SIZE..];

    // aes-gcm's own Aead::decrypt expects ciphertext||tag concatenated
    // (RFC 5116 order) -- EnvelopeAesGcm's wire format puts the tag
    // BEFORE the ciphertext, so it moves to the end here, the identical
    // reordering the plpython3u sibling function already does.
    let mut ciphertext_then_tag = Vec::with_capacity(ciphertext.len() + TAG_SIZE);
    ciphertext_then_tag.extend_from_slice(ciphertext);
    ciphertext_then_tag.extend_from_slice(tag);

    let key = Key::<Aes256Gcm>::from_slice(key_bytes);
    let cipher = Aes256Gcm::new(key);
    let nonce = Nonce::from_slice(nonce_bytes);

    cipher.decrypt(nonce, ciphertext_then_tag.as_slice()).ok()
}

/// PayloadEncryptor.EncryptLeafAsync encrypts the leaf's canonical JSON
/// text (e.g. `"42.5"`) -- always double-quoted. Strips the surrounding
/// quotes, matching every sibling implementation's own identical step.
fn strip_json_quotes(s: &str) -> &str {
    s.strip_prefix('"').and_then(|s| s.strip_suffix('"')).unwrap_or(s)
}

fn compare_datetimeoffset(a: &str, b: &str) -> Result<std::cmp::Ordering, String> {
    let a_dt = chrono::DateTime::parse_from_rfc3339(a)
        .map_err(|e| format!("could not parse DateTimeOffset value '{a}': {e}"))?;
    let b_dt = chrono::DateTime::parse_from_rfc3339(b)
        .map_err(|e| format!("could not parse DateTimeOffset value '{b}': {e}"))?;
    Ok(a_dt.cmp(&b_dt))
}

#[pg_extern(immutable, strict, parallel_safe)]
fn decrypt_and_compare_rust(
    ciphertext_base64: &str,
    key_bytes: &[u8],
    data_type: &str,
    comparison_operator: &str,
    comparison_value: &str,
) -> bool {
    use base64::Engine;
    let Ok(blob) = base64::engine::general_purpose::STANDARD.decode(ciphertext_base64) else {
        return false; // malformed base64 -- never satisfies, never errors
    };

    let Some(plaintext_raw) = decrypt_envelope_aes_gcm(&blob, key_bytes) else {
        return false; // wrong key / corrupt row -- never satisfies, never errors
    };

    let Ok(plaintext_raw_str) = std::str::from_utf8(&plaintext_raw) else {
        return false;
    };
    let plaintext = strip_json_quotes(plaintext_raw_str);

    let comparison = match data_type {
        "Number" => {
            let a: f64 = plaintext.parse().unwrap_or(f64::NAN);
            let b: f64 = comparison_value.parse().unwrap_or(f64::NAN);
            match a.partial_cmp(&b) {
                Some(o) => o,
                None => return false, // NaN on either side -- never satisfies, matches this design's own OrderRevealingEncryption stance of not defining an order for non-finite numbers
            }
        }
        "DateTimeOffset" => match compare_datetimeoffset(plaintext, comparison_value) {
            Ok(o) => o,
            Err(msg) => error!("decrypt_and_compare_rust: {msg}"),
        },
        _ => plaintext.cmp(comparison_value),
    };

    match comparison_operator {
        "gt" => comparison == std::cmp::Ordering::Greater,
        "gte" => comparison != std::cmp::Ordering::Less,
        "lt" => comparison == std::cmp::Ordering::Less,
        "lte" => comparison != std::cmp::Ordering::Greater,
        other => error!("decrypt_and_compare_rust: comparison_operator must be one of gt/gte/lt/lte, got \"{other}\""),
    }
}

#[cfg(any(test, feature = "pg_test"))]
#[pg_schema]
mod tests {
    use pgrx::prelude::*;

    // Golden fixture from tests/EventStore.SqlClr.SqlServer.Tests/
    // EncryptedPredicateFunctionsTests.cs -- real EnvelopeAesGcm.Encrypt
    // output, not invented -- proving genuine cross-runtime
    // interoperability the same way the SQL Server/plpython3u/C
    // implementations already do.
    const KEY: &str = "4fs+TJWaTTE9sx19HWvqYYXWPY072Nm/32mJxqFCYD0=";
    const NUMBER_CIPHERTEXT: &str = "LVU6ANGl+u5gD7TQb2aYy0bvGOFgUmVEAECvpP7ShJautA==";
    const DATE_CIPHERTEXT: &str = "ljAzgGlMwjhP43L9INTKooy/xc4xmqiSUWBnDso6XRQTFsiKEWB6mmYjg1ufdIQ4yWo=";

    #[pg_test]
    fn decrypts_real_envelope_aes_gcm_ciphertext_and_compares_numbers_correctly() {
        let key = Spi::get_one::<Vec<u8>>(&format!("SELECT decode('{KEY}', 'base64')")).unwrap().unwrap();
        assert!(crate::decrypt_and_compare_rust(NUMBER_CIPHERTEXT, &key, "Number", "gt", "40"));
        assert!(!crate::decrypt_and_compare_rust(NUMBER_CIPHERTEXT, &key, "Number", "gt", "50"));
        assert!(crate::decrypt_and_compare_rust(NUMBER_CIPHERTEXT, &key, "Number", "gte", "42.5"));
        assert!(crate::decrypt_and_compare_rust(NUMBER_CIPHERTEXT, &key, "Number", "lt", "100"));
        assert!(!crate::decrypt_and_compare_rust(NUMBER_CIPHERTEXT, &key, "Number", "lte", "10"));
    }

    #[pg_test]
    fn decrypts_real_envelope_aes_gcm_ciphertext_and_compares_dates_correctly() {
        let key = Spi::get_one::<Vec<u8>>(&format!("SELECT decode('{KEY}', 'base64')")).unwrap().unwrap();
        assert!(crate::decrypt_and_compare_rust(DATE_CIPHERTEXT, &key, "DateTimeOffset", "gt", "2026-01-01T00:00:00Z"));
        assert!(!crate::decrypt_and_compare_rust(DATE_CIPHERTEXT, &key, "DateTimeOffset", "gt", "2026-12-01T00:00:00Z"));
        assert!(crate::decrypt_and_compare_rust(DATE_CIPHERTEXT, &key, "DateTimeOffset", "lte", "2026-03-15T00:00:00Z"));
    }

    #[pg_test]
    fn a_wrong_key_returns_false_rather_than_erroring() {
        let wrong_key = vec![0u8; 32];
        assert!(!crate::decrypt_and_compare_rust(NUMBER_CIPHERTEXT, &wrong_key, "Number", "gt", "0"));
    }
}

#[cfg(test)]
pub mod pg_test {
    pub fn setup(_options: Vec<&str>) {}
    pub fn postgresql_conf_options() -> Vec<&'static str> {
        vec![]
    }
}
