[← ADR index](../07-adrs.md)

# ADR-057: GDPR/CCPA erasure via crypto-shredding — per-entity data-encryption keys, destroyed on request

Status: Accepted — **revises `ADR-009`'s no-erasure stance**

Context: `ADR-009` originally decided there would be no deletion/erasure
mechanism of any kind — masking was framed as the only redaction
primitive, and a real erasure requirement was named as "a deliberately
unsolved, separate problem." Direction received this session reverses
that: **erasure is now a real requirement.** Stated approach: something
"as simple as the entity dereference so the data recording can be kept
without personally identifiable information" — i.e., keep the append-only
event history (counts, structure, non-identifying fields, the hash chain)
intact forever, but make the *identifying* content of a specific subject's
data permanently unrecoverable.

This is exactly the shape of **crypto-shredding**: encrypt personal-data
fields with a key scoped to the data subject, held separately from the
data itself; erasure destroys the key, not the row. [Mathias Verraes —
"Eventsourcing Patterns: Crypto-Shredding"](https://verraes.net/2019/05/eventsourcing-patterns-throw-away-the-key/)
is the canonical write-up of this pattern for event-sourced systems
specifically. **Regulatory recognition, checked rather than assumed**:
the European Data Protection Board (Guidelines 5/2019), the UK ICO, and
the French CNIL all explicitly recognize cryptographic erasure as valid
GDPR Art. 17 compliance, conditioned on strong encryption (AES-256),
*irreversible* key destruction, and an auditable destruction record.
**One honest caveat, not silently dropped**: Verraes himself notes some
readings hold that encrypted personal data is still personal data under
GDPR's letter — crypto-shredding is the accepted practical answer across
three national regulators, not a legally bulletproof one in every
jurisdiction. Direction received treats this as an accepted, informed
trade-off, not a blocker.

Decision:
- **Erasure is scoped to `EntityId`, matching this design's existing
  entity-centric model (`ADR-021`)** — "erase entity X" is the operation,
  not a separate subject-identity concept. **Consequence worth naming
  explicitly**: if a schema's `EntityIdField` (`ADR-021`) is itself
  derived from PII (e.g., an email address used directly as the entity
  key), erasing the entity doesn't erase the identifier itself, only the
  classified *payload* fields. New domains adopting this framework should
  prefer an opaque/synthetic `EntityId` (a GUID) precisely so entity
  erasure fully removes identifiability without needing to also touch
  the identifier.
- **Every property already carrying `x-masking.regulatoryClassification`
  (`ADR-009`) is now also encrypted at rest**, not just claims-gated at
  read time — the schema doesn't need new classification metadata,
  reusing what `ADR-009` already requires a schema author to declare.
  Encryption happens **after** `SchemaValidationService` validates the
  real plaintext value against its declared type (validation is
  unaffected), and **before** the payload is written to `StoredEvent.
  Payload` and hashed. **`ADR-019`'s hash chain is completely
  unaffected** — `PayloadHash`/`ChainHash` are computed over the payload
  exactly as stored (ciphertext for classified fields, plaintext for
  everything else), the same as always; erasure never retroactively
  touches a hash, a chain link, or `Payload` itself, ever — only whether
  a given field's *ciphertext* can still be decrypted changes over time.
- **One new, optional `x-masking` field: `erasureScope`** — a JSON
  Pointer to another property in the *same* payload naming the `EntityId`
  whose key protects this field, for the case where the PII belongs to a
  different entity than the event's own (e.g., `OrderPlaced.customerName`
  is the *customer's* data, not the *order's* — it declares
  `"erasureScope": "$.customerId"`; a field with no `erasureScope`
  defaults to the event's own `EntityId`, the common case). This is a
  fifth repeated-relationship-shaped envelope/schema field alongside
  `parentEventIds`/`MaterializationOfEventId`/`TelemetryPointer`/
  `AttachmentRef` (`CLAUDE.md`'s running list) — it answers a distinct
  question ("whose key encrypts this specific value") and is deliberately
  not folded into any of the other four.
- **Envelope encryption, one Data-Encryption Key (DEK) per `(AppId,
  EntityId)`**, generated the first time a classified field is published
  for that entity. The DEK itself is wrapped by a master Key-Encrypting-
  Key (KEK) held in a real key-management backend — **kept pluggable via
  an `IErasureKeyStore` seam** (the same Strategy-pattern shape `ADR-009`/
  `ADR-052` already established, keyed-registered per `ADR-041`'s
  composition root), not hardcoded to one vendor, since this design
  hasn't committed to a single cloud KMS anywhere else either. A
  deployment backs it with Azure Key Vault, AWS KMS, HashiCorp Vault (the
  vendor a sibling implementation, [HashiCorp's own GDPR-compliant
  event-sourcing write-up](https://www.hashicorp.com/en/resources/gdpr-compliant-event-sourcing-with-hashicorp-vault),
  independently arrives at the same per-subject-key shape), or a simple
  local encrypted store for dev.
- **Read path**: `IPayloadMasker` (`ADR-009`'s existing enforcement
  point) gains one more step for a caller who *holds* the field's
  `RequiredClaim` — after the existing claim check passes, decrypt the
  ciphertext using the entity's DEK (fetched via `IErasureKeyStore`)
  before returning `{"value": ...}`. **Claims-based masking is completely
  unaffected and unaware this exists** — a caller lacking the claim still
  gets `{"masked": ...}` exactly as `ADR-009` already specifies, whether
  or not the underlying field happens to also be encrypted.
- **The wrapper's `oneOf` grows a third branch: `{"erased": true}`** —
  deliberately distinct from `{"masked": ...}`. `masked` means "you lack
  a claim; someone with the right claim can still see this." `erased`
  means "this was permanently destroyed; **no one** can ever see it
  again, including someone who held every claim." Conflating the two
  would misrepresent an irreversible fact as an ordinary permission gap —
  the same "gates the value, never the existence" discipline `ADR-052`
  already applies to streaming redaction, extended one step further:
  here the *value itself* stops existing in any readable form, and the
  wrapper must say so plainly rather than reuse `masked`'s softer signal.
- **Erasure is itself a permanent, auditable record — an event, not a
  side effect.** Requesting erasure for an `EntityId` publishes a
  reserved `EntityErasureRequested` event (ordinary `StoredEvent`, hash-
  chained like everything else) recording *when* and *by whom*, then
  destroys that entity's DEK via the configured `IErasureKeyStore`'s own
  irreversible key-destruction primitive (Key Vault purge, KMS scheduled
  deletion, Vault key deletion). The fact that erasure happened is
  preserved forever, consistent with `README.md`'s governing "never lose
  data" principle — only the erased *content* is gone.

Consequences:
- **`ADR-009`'s "no deletion mechanism, and none is wanted" section is
  superseded by this ADR** — struck through there, not deleted, per this
  project's additive-history convention. `README.md`'s "What this system
  deliberately is not" bullet on erasure is updated the same way.
- **The key store becomes a new, critical authoritative store** — folds
  into `ADR-056`'s data-lifecycle classification directly: losing
  `IErasureKeyStore`'s contents *without* any erasure request having
  happened is equivalent to accidentally erasing every subject at once.
  Its own durability/backup posture matters exactly as much as the Event
  Log's — an inverse risk to erasure itself, worth stating plainly rather
  than only celebrating the feature it enables.
- Every classified field now carries real encrypt/decrypt cost on every
  publish and every claim-holding read — accepted, since it's the
  mechanism that makes erasure possible at all; unclassified fields (the
  overwhelming majority of most payloads) are entirely unaffected.
- Resolves `docs/10-open-questions.md`'s erasure row.
- `docs/data/entity-store.md` gains the `EntityErasureKey` entity
  (wrapped-DEK metadata — never the key material itself, which lives only
  in the configured `IErasureKeyStore`) — done this pass.
- Does not attempt GDPR/CCPA compliance as a whole — only the specific,
  previously-declined mechanism (erasure of previously-durable event
  data) this ADR was asked to solve. Consent management, data portability
  (Art. 20), and processing-purpose limitation remain entirely outside
  this design's scope, same as before.
