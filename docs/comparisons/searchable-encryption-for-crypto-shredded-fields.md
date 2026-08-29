[← Comparisons index](README.md)

# How should equality and range queries work against crypto-shredded fields?

**Raised by:** direct request — support for encrypting event payload
fields for a referenced key, with the ability to still run performant
equality and range queries against the encrypted values, and clean up
the resulting search indexes as part of crypto-shredding.

`ADR-057` already encrypts every `x-masking.regulatoryClassification`
field with a per-`(AppId, EntityId)` data-encryption key (DEK), via
envelope AES-GCM (`EnvelopeAesGcm`) and a pluggable `IErasureKeyStore`.
That mechanism has no secondary search structure at all today — a
classified field's ciphertext sits inside `Payload`, and the existing
`FilterableField`/`IJsonPathTranslator` query-pushdown pipeline
(`04-odata-filter-pushdown.md`) can only extract and compare it as
opaque ciphertext. This fork is about what to add so equality and range
queries still work, without decrypting a whole table into application
memory to filter.

Two facts, verified this session, shape every option below:

- `EnvelopeAesGcm`'s nonce is deterministic
  (`HMACSHA256(key, plaintext)[..12]`, required so `ADR-011`'s
  idempotency hash is stable across retries) — so equal plaintexts
  already produce identical ciphertext **within one entity's key**. That
  gives free equality search *inside* one already-known entity, but not
  the actually-useful case: finding which entities/events match a value
  *across* many entities, each independently crypto-shreddable.
- Property-preserving encryption (deterministic encryption, OPE, ORE)
  has a real, demonstrated break: [Naveed, Kamara, Wright — "Inference
  Attacks on Property-Preserving Encrypted Databases," CCS
  2015](https://www.microsoft.com/en-us/research/publication/inference-attacks-property-preserving-encrypted-databases/)
  recovers **exact plaintext**, not a fuzzy guess, for low-cardinality
  columns (birthdate, zip code, a diagnosis code) via frequency+order
  analysis against public auxiliary distributions — their "cumulative
  attack" achieves near-complete recovery on exactly this shape of
  field. High-cardinality fields (a name, a long free-text value) are
  meaningfully more resistant to the same attack, since there's no small
  closed value space for frequency/order analysis to exploit.

## The fork

### Option A — Keyed HMAC blind index (equality)

| | |
|---|---|
| **Pros** | No new cryptography — reuses the already-adopted `Microsoft.Extensions.Compliance.Redaction` `HmacRedactor` (`ADR-009`'s `Hash` masking strategy uses the same primitive). Deterministic, so an ordinary expression index/computed column works on every provider, identical mechanism to today's plaintext `FilterableField` indexing. Real, widely-used prior art: [CipherSweet](https://ciphersweet.paragonie.com/internals/blind-index) (Paragon Initiative)'s blind-indexing design, [CryptDB](https://dl.acm.org/doi/10.1145/2043556.2043566) (Popa et al., SOSP 2011)'s deterministic-encryption layer, [SQL Server Always Encrypted](https://learn.microsoft.com/en-us/sql/relational-databases/security/encryption/always-encrypted-database-engine)'s deterministic-encryption equality support. |
| **Cons** | Only equality — no ordering information at all, by design. Needs a token comparable across entities, which forces a key-scope decision (see the second fork below) distinct from `ADR-057`'s existing per-entity DEK. |

### Option B — Bucketed/fuzzy deterministic range index

| | |
|---|---|
| **Pros** | Same primitive as Option A (keyed HMAC), applied at multiple granularities (e.g. year/month/day for a date, configurable bucket width for a number) — no new cryptography, no order information ever leaves the ciphertext beyond coarse bucket membership. Real, verified prior art for exactly this shape: CipherSweet's own "fuzzy indexing"/Bloom-filter approach to range-like queries over blind-indexed data. Composes cleanly with a cardinality-aware guardrail (a birthdate can use wide buckets or be refused outright; a high-cardinality numeric field can use narrow ones). |
| **Cons** | Not a true range index — a query still needs an exact-match step (decrypt-and-compare) over the bucket's candidate rows, which is coarser/wider than a real ordered index would give. Bucket width is a real, visible privacy/performance trade-off a schema author has to choose deliberately, not a free parameter. |

### Option C — Real Order-Revealing Encryption (ORE)

| | |
|---|---|
| **Pros** | Lets the database compare ciphertexts **directly**, with zero decryption needed to evaluate a predicate — the strongest fit for "run inside the database, don't pull ciphertext into app memory to filter." A real, published construction exists: [Chenette, Lewi, Weis, Wu — "Practical Order-Revealing Encryption with Limited Leakage," FSE 2016](https://eprint.iacr.org/2016/661) and [Lewi, Wu — "Order-Revealing Encryption: New Constructions, Applications, and Lower Bounds," CCS 2016](https://eprint.iacr.org/2016/612.pdf) (the CLWW/Lewi-Wu left/right-ciphertext scheme), with materially less leakage than plain OPE. |
| **Cons** | No vetted .NET library implements it — building the CLWW/Lewi-Wu construction is this project's first bespoke, unvetted cryptographic primitive. A published leakage-abuse break exists **specifically against ORE**: [Grubbs, Sekniqi, Bindschaedler, Naveed, Ristenpart — "Leakage-Abuse Attacks Against Order-Revealing Encryption," 2016](https://eprint.iacr.org/2016/895.pdf). Same exact-recovery risk as Option D below applies to low-cardinality fields specifically. |

### Option D — Plain Order-Preserving Encryption (OPE)

| | |
|---|---|
| **Pros** | The original, most-studied construction: [Boldyreva, Chenette, Lee, O'Neill — "Order-Preserving Symmetric Encryption," Eurocrypt 2009](https://faculty.cc.gatech.edu/~aboldyre/papers/bclo.pdf) (the POPF security notion), refined by [the same authors' 2011 revisit](https://eprint.iacr.org/2012/625). Also enables direct in-database ciphertext comparison, like ORE. |
| **Cons** | Strictly more leakage than ORE for the same capability (ciphertext order is fully revealed, not just revealed via an explicit compare function) — every reason to accept ORE's risk applies here with a worse leakage profile and no offsetting benefit. Same lack of a vetted .NET library. Superseded by Option C for this design. |

### Option E — Secure enclaves (trusted execution environment)

| | |
|---|---|
| **Pros** | The industry's actual production answer to this exact problem — [SQL Server Always Encrypted with secure enclaves](https://learn.microsoft.com/en-us/sql/relational-databases/security/encryption/always-encrypted-enclaves) runs real comparisons on plaintext *inside* a hardware-attested enclave on the server, avoiding both the leakage-abuse risk above and any bespoke cryptography. |
| **Cons** | SQL Server-only — no equivalent exists for PostgreSQL or SQLite in this design's three-provider lineup, a hard incompatibility with `ADR-001`'s per-deployment-build-per-provider requirement. Rejected outright on this basis, not on any cryptographic merit. |

## Recommendation

**Adopt A + B as the default, safe mechanism (queued ADR); adopt C as a
genuine, opt-in, loudly-gated alternative per direct request to have a
real working example of both (queued ADR); reject D (superseded by C)
and E (breaks the three-provider requirement).**

The honest trade-off being accepted: Option C (ORE) is real and useful
specifically because it satisfies "as much in-database as possible"
better than A+B can — A+B still needs an exact-match decrypt step (ideally
also pushed into the database via a native evaluator, but a real step
regardless) after the bucket narrows candidates, while ORE lets the
database's own comparison operator decide the predicate on ciphertext
alone. That benefit is paid for with a published, exact-plaintext-
recovering attack, which is why the queued ORE ADR gates it far more
strictly than the bucketed approach — refusing it outright on any field
carrying `x-masking.regulatoryClassification`, not merely warning. A+B's
own guardrail is cardinality-aware rather than a blanket classification
rule, since the attack's power is concentrated in low-cardinality value
domains specifically, not classification alone.
