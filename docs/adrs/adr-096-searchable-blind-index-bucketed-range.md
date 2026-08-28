[← ADR index](../07-adrs.md)

# ADR-096: Searchable blind-index (equality) and bucketed-range indexes over crypto-shredded fields

Status: Accepted — extends `ADR-057`

Context: `ADR-057` encrypts every `x-masking.regulatoryClassification`
field at rest with a per-`(AppId, EntityId)` data-encryption key (DEK),
but built no secondary search structure — a classified field's
ciphertext sits inside `Payload`, opaque to `04-odata-filter-pushdown.md`'s
existing `FilterableField`/`IJsonPathTranslator` pushdown mechanism.
Direct request: support equality and range queries against these fields
without decrypting large amounts of data into application memory, with
the resulting search structures cleanable as part of crypto-shredding.
See `docs/comparisons/searchable-encryption-for-crypto-shredded-fields.md`
for the full option comparison (Options A and B here; `ADR-097` covers
Option C, ORE) — this ADR is that comparison's deciding record for the
safe default path.

Decision:
- **New optional schema extension, `x-masking.searchable`**, alongside
  the existing `x-masking` object a classified property already carries
  (`ADR-009`/`ADR-057`):
  ```json
  "CustomerEmail": {
    "type": "string",
    "x-masking": { "requiredClaim": "pii:view", "regulatoryClassification": "PII" },
    "x-masking-searchable": {
      "indexKind": "Equality",
      "keyScope": "Shared"
    }
  }
  ```
  A `Range`-kind field also carries `bucketGranularities` (below) and a
  required `cardinality` hint (see the guardrail below). A field with no
  `x-masking-searchable` is completely unaffected — this is purely
  additive to `ADR-057`'s existing encrypt-at-rest behavior.
- **`indexKind`: `"Equality"` or `"Range"`.**
  - `Equality` computes one token: a keyed HMAC of the canonical plaintext
    value, reusing `ADR-009`'s already-adopted `Microsoft.Extensions.
    Compliance.Redaction` `HmacRedactor` — the same primitive, not a
    second hashing mechanism.
  - `Range` computes **multiple** tokens, one per configured
    `bucketGranularities` entry (e.g. `["Year", "Month", "Day"]` for a
    `DateTimeOffset`, or a list of numeric bucket widths for a `Number`)
    — the same HMAC primitive applied to the bucket label instead of the
    exact value. A range query decomposes into equality lookups against
    the coarsest granularity that still narrows usefully, then an exact
    decrypt-and-compare step over the (small) candidate set — see
    "Query routing" below.
- **`keyScope`: `"Shared"` or `"PerEntity"`** — a genuine, per-field
  choice, not a single global answer, per direct request to support
  both:
  - `"Shared"` — one HMAC key per `(AppId, EventTypeName, FieldJsonPath)`,
    held by a new `ISearchIndexKeyStore` seam (same Strategy/keyed-DI
    shape as `IErasureKeyStore`, but a **distinct lifecycle**: this key
    is not auto-destroyed per entity — manual rotation only, stated
    plainly as a lifecycle gap the same way `ADR-057` names its own
    key-store risk explicitly rather than glossing over it). This is
    what makes a real cross-entity search possible ("find every order
    for this customer email") — the token is comparable across every
    entity's independently-encrypted `Payload`.
  - `"PerEntity"` — the token is derived from the same per-entity DEK
    material `ADR-057` already manages, via HKDF with a fixed,
    purpose-distinct info string (`"searchable-index-v1"`) so the derived
    key is never the same bytes used for `EnvelopeAesGcm` payload
    encryption. Destroyed automatically the instant `ErasureKeyService.
    EraseAsync` destroys that entity's DEK — true crypto-shredding,
    matching `ADR-057`'s model exactly. **Named limitation, not a defect**:
    this only ever answers "does this one already-known entity have
    value V," never "which entities have value V" — a cross-entity
    `where` filter cannot be satisfied this way, since a query has no
    way to know in advance which entity's key to derive a comparison
    token under.
- **Erasure cleanup is a real delete of a derived, rebuildable
  structure, not cryptographic destruction, for `Shared`-scope tokens.**
  `EntityErasureResolver` (`ADR-057`) gains one more side-effect step
  alongside `ErasureKeyService.EraseAsync`: delete every
  `EncryptedFieldIndexEntry` row (below) belonging to the erased
  `EntityId`. This is deliberately the same category of operation as
  rebuilding a CQRS read model (`patterns/cqrs-and-materialized-views.md`)
  — a derived structure, never the source of truth. **Confirmed this
  session, not assumed**: `ADR-019`'s `ChainHash` and `ADR-033`'s
  Merkle-tree replication-sync are both computed only over
  `StoredEvent`/`Payload`; neither is aware this table exists, so
  deleting its rows never touches either integrity mechanism. `Payload`
  itself stays crypto-shredded via `ADR-057`, completely unchanged —
  this ADR never mutates or removes an event row. `PerEntity`-scope
  tokens need no separate delete step: they become permanently
  uncomputable the instant the owning DEK is destroyed, the same as any
  other `PerEntity`-scoped ciphertext.
- **New data-model entity, `EncryptedFieldIndexEntry`** (derived,
  rebuildable — same category as a CQRS read model, not part of the
  Event Log): `{EntityId, AppId, EventTypeName, FieldJsonPath, IndexKind,
  Granularity (Range only, else null), Token, StoredEventSequenceNumber}`.
  Landed in `docs/data/entity-store.md` and given a `DbSet` registration
  in the same pass as this ADR, per this project's standing rule that the
  deciding ADR is the shape authority and must not defer the matching
  data-model edit.
- **`FilterableField` (`docs/data/schema-registry.md`) gains an
  `IndexKind` discriminator**: `PlaintextExpression` (today's only value,
  the existing `json_extract`/`->>`/`JSON_VALUE` mechanism, unchanged
  default) or `EncryptedBlindIndex`/`EncryptedRangeBucket`. A field
  flagged with either encrypted kind routes `GraphQlFilterPredicateBuilder`
  to compare against `EncryptedFieldIndexEntry.Token` instead of
  extracting straight from ciphertext-filled `Payload` — extracting a
  classified field's ciphertext via `json_extract`/`->>`/`JSON_VALUE`
  today would only ever compare opaque bytes, silently returning wrong
  results, which this discriminator prevents by construction rather than
  documentation alone.
- **Query routing for `Range`**: an `eq` clause against an
  `EncryptedBlindIndex` field is one equality lookup on
  `EncryptedFieldIndexEntry.Token` (`IndexKind = Equality`). A
  `gt`/`gte`/`lt`/`lte` clause against an `EncryptedRangeBucket` field
  first narrows candidates via an equality lookup at the coarsest
  granularity whose bucket boundary the comparison value falls strictly
  inside (e.g. a `> 2026-03-15` query against Year/Month/Day
  granularities narrows to the matching Year+Month buckets, then the
  boundary month itself needs the Day granularity to exclude values
  before the 15th), then an exact decrypt-and-compare step over that
  narrowed candidate set. That exact-match step is the seam `ADR-098`
  names — its default app-tier implementation only ever runs over this
  already-narrowed set, never a full-table decrypt.
- **Registration-time guardrail, cardinality-aware.** The real risk
  driver, verified against the actual attack paper this session, is a
  small, guessable value domain — a birthdate/zip-code/diagnosis-code
  column is fully recoverable via frequency+order analysis against
  public auxiliary distributions once bucket membership is visible; a
  high-cardinality field (a name, a long free-text value) is
  meaningfully more resistant to the identical attack. `x-masking-
  searchable` on a `Range`-kind field requires a declared `cardinality:
  "Low" | "High"` hint. `Low` cardinality combined with `x-masking.
  regulatoryClassification` present refuses registration (`400`) unless
  `acknowledgeLeakageRisk: true` is also set on the same extension object
  — mirrors `ADR-071`'s PCI-SAD registration-time refusal precedent for a
  mechanism that structurally undermines a classification's own purpose.
  `High`-cardinality classified fields are permitted without the
  override, since the bucketed approach's leakage against them is far
  weaker — a blanket rule would over-restrict a name field while
  under-warning on a birthdate field, which this cardinality split is
  specifically written to avoid.

Consequences:
- Extends `ADR-057`'s crypto-shredding without changing anything about
  it — `Payload` encryption, `IErasureKeyStore`, and the `{value}`/
  `{masked}`/`{erased}` read-path wrapper are all unaffected.
- `docs/extensibility-points.md` gains `ISearchIndexKeyStore` alongside
  `IErasureKeyStore` — same registration model (`ADR-059`), different
  lifecycle (manual rotation, not auto-destroyed per entity).
- The `Shared` key-scope's own lifecycle gap (no automatic rotation) is a
  real, accepted risk, same honesty this design already applies to
  `IErasureKeyStore`'s own critical-store status (`ADR-057`'s
  Consequences) — a deployment choosing `Shared` scope for a field is
  choosing to own that key's rotation discipline operationally.
- Does not build any in-database native evaluator — the exact-match step
  after bucket-narrowing defaults to an app-tier decrypt over a small
  candidate set; `ADR-098` designs (but does not build) a pluggable
  native alternative.
- `08-build-plan.md` gains a new, Not-started, named item for this ADR's
  implementation, depending on GDPR/CCPA Erasure via Crypto-Shredding,
  Property-Level Masking, and Follow API + Filter Pushdown.

**Compliance note**: the cardinality-aware guardrail directly answers a
real HIPAA/GDPR exposure this project's own Vitals (clinical trials/
device telemetry) proving-ground domain would otherwise create —
birthdate, a classic HIPAA Safe Harbor identifier (`45 CFR
§164.514(b)(2)`, cited in `ADR-052`), is exactly the low-cardinality
shape the underlying attack fully recovers.

**Implementation note, added 2026-08-27**: built this session
(`08-build-plan.md` item 54). Two real corrections found only by actually
building it, neither changing the Decision above, both landed the same
pass:
- **`ISearchIndexKeyStore`'s real shape is `ComputeHmacAsync(keyReference,
  data) -> byte[]`, not an envelope encrypt/decrypt pair.** A Shared-scope
  index key computes a deterministic MAC over arbitrary future values
  (identically at publish and query time) — it never encrypts data at
  rest, so there's no confidentiality reason to hide it behind
  `IErasureKeyStore`'s envelope indirection. Mirrors a real KMS primitive
  that exists for exactly this (Vault Transit's `/hmac` endpoint, AWS
  KMS's `GenerateMac`), not a hypothetical shape invented for this ADR.
- **`PerEntity` key derivation, concretely**: `IErasureKeyStore` never
  exposes raw DEK bytes (by design). The actual mechanism is
  `SHA-256(backend.EncryptAsync(keyReference, a fixed info string))` —
  deterministic, entity-key-dependent, and permanently uncomputable the
  instant `EncryptAsync` under a destroyed `keyReference` starts failing,
  which is exactly the "destroyed alongside the DEK" property this scope
  requires, through the real interface shape rather than a literal HKDF.
- `EncryptedFieldIndexEntry.Token` is a base64 `string`, not `byte[]` —
  matches this codebase's existing base64-ciphertext convention
  (`PayloadEncryptor` already stores ciphertext this way) and is portable
  across all three providers as an ordinary indexed text column.
- **Found and fixed as a prerequisite, not a scope addition**:
  `PayloadEncryptor` (`ADR-057`'s own encryption) was never actually
  registered in any Host's DI — every real `PublishService` resolved it
  as `null`, so classified-field encryption was inert in production,
  exercised only by test code that constructs it directly. Fixed inside
  `ErasureServiceCollectionExtensions.AddErasure`, the one place every
  Host already calls for this wiring.
- `GraphQlFilterPredicateBuilder.Build` became `async` — resolving
  matches from `EncryptedFieldIndexEntry` needs a database query, which
  the pre-existing synchronous signature couldn't accommodate.
- **A bucketed range query requires both a lower and upper bound in the
  same clause.** A one-sided range (`gt` alone, no `lt`/`lte`) would
  require enumerating an unbounded number of buckets — refused with a
  clear error instead of attempting it, rather than silently accepting a
  pathological query.
- **Cloud/Vault `ISearchIndexKeyStore` backends, added later the same
  day**: rather than reimplementing four cloud SDKs a second time,
  `CloudSearchIndexKeyStoreAdapter` wraps any existing `IErasureKeyStore`
  backend (Azure Key Vault, AWS KMS, Google Cloud KMS, HashiCorp Vault —
  all four already expose exactly `CreateKeyAsync`/`EncryptAsync`, the two
  primitives a search-index key needs) via the same key-derivation trick
  `PerEntity` scope already uses: encrypt a fixed label under the
  cloud-managed key, hash the ciphertext into a local, single-use derived
  key, HMAC the caller's real data with that. One mechanism, not four
  bespoke ones — registered in `ErasureServiceCollectionExtensions`
  alongside each corresponding erasure backend, sharing the same SDK
  client instance, with its own independent `SearchIndexKey` reference
  (no lifecycle coupling with any entity's own DEK despite sharing the
  underlying client). Verified against `LocalErasureKeyStore` (provider-
  agnostic logic, no real cloud credentials needed to test it):
  deterministic per `(keyReference, data)`, different for different keys
  or data, and fails once the wrapped key is destroyed — the same
  "destroyed alongside the DEK" property `PerEntity` scope's own direct
  use of this trick already relies on.
- Correctness verified via `EventStore.IntegrationTests`
  (`SearchableEncryptionSqliteTests`): an equality query matches without
  ever extracting `Payload` as plaintext; the registration guardrail
  refuses a `Low`-cardinality classified field without
  `acknowledgeLeakageRisk`; erasure removes a `Shared`-scope entity's own
  index rows with `ChainHash` provably unchanged before/after.
