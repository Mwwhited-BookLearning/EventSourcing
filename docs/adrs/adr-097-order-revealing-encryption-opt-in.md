[← ADR index](../07-adrs.md)

# ADR-097: Order-Revealing Encryption (ORE) as an opt-in, loudly-gated real range-comparison mechanism

Status: Accepted — sibling to `ADR-096`, not a revision of it

Context: `ADR-096` adopted a bucketed deterministic index as the safe
default for range queries over crypto-shredded fields, deliberately
accepting a coarser, decrypt-after-narrow query shape in exchange for no
new cryptography. Direct request: build a real, working example of the
riskier alternative too — actual Order-Revealing Encryption — specifically
because it satisfies "run as much inside the database as possible" more
completely than `ADR-096` can: the database can compare ORE ciphertexts
directly, with **zero decryption needed to evaluate the predicate at
all** (only matched rows are ever decrypted, and only for a caller
holding the field's claim). See `docs/comparisons/searchable-encryption-
for-crypto-shredded-fields.md` (Options C and D) for the full comparison
this ADR decides.

Decision:
- **Build the CLWW/Lewi-Wu left/right-ciphertext ORE construction** —
  [Chenette, Lewi, Weis, Wu, FSE 2016](https://eprint.iacr.org/2016/661)
  and [Lewi, Wu, CCS 2016](https://eprint.iacr.org/2016/612.pdf) — chosen
  over plain OPE (`ADR-096`'s comparison, Option D) because it leaks
  strictly less for the same range-comparison capability: OPE's
  ciphertext directly encodes full order; ORE's comparison result is
  only revealed by an explicit public compare function over a
  ciphertext pair, not baked into the ciphertext's own byte ordering.
  **This is this project's first bespoke, unvetted cryptographic
  primitive** — no vetted .NET library implements either construction,
  checked this session — stated plainly, not glossed over, the same way
  `ADR-057` names format-preserving encryption's declined key-management
  cost explicitly.
- **Selectable per field via `x-masking-searchable`**: `"indexKind":
  "OrderRevealing"` instead of `"Range"` — a field chooses one or the
  other, never both, since they answer the identical query shape
  (`gt`/`gte`/`lt`/`lte`) with a different mechanism and risk profile.
  Same `keyScope` choice as `ADR-096` (`Shared` for cross-entity search,
  `PerEntity` derived via HKDF off the entity's own DEK for true
  crypto-shredding, same single-entity-only limitation named there).
- **Stricter guardrail than `ADR-096`, deliberately, with no override.**
  Registration refuses (`400`) `"indexKind": "OrderRevealing"` outright on
  any field also carrying `x-masking.regulatoryClassification` — no
  `acknowledgeLeakageRisk`-style escape hatch. This is a real, considered
  difference from `ADR-096`'s cardinality-gated-but-overridable rule, not
  an oversight: [Naveed/Kamara/Wright, CCS 2015](https://www.microsoft.com/en-us/research/publication/inference-attacks-property-preserving-encrypted-databases/)'s
  exact-recovery attack applies to any property-preserving scheme,
  and a dedicated [leakage-abuse break against ORE specifically](https://eprint.iacr.org/2016/895.pdf)
  (Grubbs, Sekniqi, Bindschaedler, Naveed, Ristenpart, 2016) exists on
  top of that — full ciphertext-order comparison leaks strictly more per
  query than a bucket-membership check does, so an override flag would
  ask a schema author to accept a worse, less-bounded risk than
  `ADR-096`'s for the same regulated-field shape. An unclassified,
  high-cardinality field (a synthetic numeric key, a non-personal
  measurement) is unaffected and may use `OrderRevealing` freely.
- **The database compares ciphertext directly** — a `gt`/`gte`/`lt`/`lte`
  clause against an `OrderRevealing`-indexed field becomes a native
  comparison over the stored ORE ciphertext column (an ordinary indexed
  byte-comparable column on every provider, since the CLWW/Lewi-Wu
  scheme's own compare function is exactly "compare these byte strings
  in a defined way" — no `IJsonPathTranslator`/`JsonFunctions` extraction
  involved, since the value being compared already *is* the indexed
  column, not something pulled from `Payload` at query time). Only the
  final matched rows are decrypted, through the ordinary
  `ADR-057`/`ADR-009` read-path wrapper (`{value}`/`{masked}`/`{erased}`)
  for a caller who holds the field's claim.
- **Erasure**: identical shape to `ADR-096` — `Shared`-scope ORE
  ciphertext rows are deleted by `EntityErasureResolver`'s new erasure
  side-effect step (a derived, rebuildable structure, same as
  `EncryptedFieldIndexEntry`); `PerEntity`-scope ORE ciphertext becomes
  permanently uncomputable/unreadable the instant its owning DEK is
  destroyed.

Consequences:
- A genuinely riskier, opt-in sibling to `ADR-096`, not a replacement —
  a schema author explicitly trades leakage exposure for "the database
  never needs to decrypt to filter," and only for fields that pass the
  stricter, override-free guardrail above.
- The bespoke ORE implementation itself (the compare function, key
  derivation, ciphertext format) needs its own dedicated correctness/
  security review before it ships in `08-build-plan.md`'s matching
  build-plan item — named here as a real requirement, not assumed
  satisfied by this ADR's design-level decision.
- `docs/references.md` gains adopted-with-caveats rows for CLWW/Lewi-Wu
  ORE, and a considered-and-rejected row for plain OPE (superseded by
  this choice) — done this pass.
- `08-build-plan.md` gains a new, Not-started, named item, depending on
  `ADR-096`'s own item (shares the `x-masking-searchable` schema
  extension and `EncryptedFieldIndexEntry`-adjacent data model).

**Implementation note, added 2026-08-27**: the CLWW/Lewi-Wu-inspired
construction, the no-override registration refusal, `PayloadIndexer`'s
`OrderRevealing` branch, and query routing are all built this session
(`src/EventStore.Erasure/OrderRevealingEncryption.cs`) — see that file's
own header for the honest, explicit statement of what this is and isn't
(a from-scratch, testable realization of the same high-level idea, not a
verified byte-for-byte implementation of either paper). Order-preservation
correctness is verified by `EventStore.UnitTests.
OrderRevealingEncryptionTests` across many Number/DateTimeOffset pairs;
the no-override guardrail is verified by
`SearchableEncryptionSqliteTests`. ~~**One real, named scope limit found
while building the query side**: the default app-tier evaluator
(`GraphQlFilterPredicateBuilder.ResolveOrderRevealingMatchesAsync`)
compares ciphertext **in application memory** across a field's own
indexed rows, not via a native SQL comparison operator — `Compare` is a
custom byte-array function no provider's query engine understands
natively.~~ **Corrected, 2026-09-04** — see this ADR's own additive note
below: this had actually regressed further than described (a full
in-memory scan, not merely "app-tier compare"), and the "no query engine
understands it natively" framing was itself wrong — fixed for real, not
merely reframed. This is still a genuine win over the bucketed approach
(only small ciphertext tokens are read, never `Payload`, and nothing is
ever decrypted to filter). **This item is built but not marked Done in
`08-build-plan.md`** — its own exit criteria require a dedicated
security review this pass did not perform, named explicitly rather than
implied by tests passing.

**Compliance note**: the no-override guardrail is a direct, deliberate
answer to the same HIPAA Safe Harbor exposure `ADR-096`'s compliance note
names — this ADR judges the exact-recovery risk on a regulated
low-cardinality field too severe for a schema author's own risk
acceptance to responsibly gate, unlike `ADR-096`'s coarser bucket
leakage.

**Additive note, 2026-09-04 — the dedicated review this ADR's own build-
plan item requires happened, plus a real query-side fix**: an AI-assisted
code-level review found and fixed two real correctness bugs (NaN/±Infinity
silently accepted with an undefined ordering; `+0.0`/`-0.0` encoding to
different, orderable ciphertext despite `double.CompareTo` treating them
as equal) — see `docs/08-build-plan.md`'s own item for the full account
and the explicit, non-overclaimed judgment that a solo AI review does not
substitute for an independent human cryptographer's review of a novel
construction, still genuinely recommended before production use.

Separately, this pass also corrected the "no provider's query engine
understands natively" framing two paragraphs above — that undersold what
this construction actually allows. `OrderRevealingEncryption.Compare` is
empirically a **pure lexicographic byte-array comparison** (0/40,000
mismatches against a raw `memcmp`-style check across random pairs), which
means an ordinary database string/binary comparison operator already
computes the identical result, given ciphertext bytes are encoded as a
value whose own character order agrees with byte order. Base64 (the
original `EncryptedFieldIndexEntry.Token` encoding) does NOT have this
property (532/40,000 mismatches); fixed-width uppercase hex does (0/40,000
mismatches) — switched to hex, and `GraphQlFilterPredicateBuilder.
ResolveOrderRevealingMatchesAsync` now pushes a real `.Where` clause down
to native SQL instead of pulling every row into application memory.
Verified for real against SQLite, PostgreSQL, and SQL Server (Testcontainers
for the latter two) — SQL Server's own default collation was a genuine
risk worth checking, not assuming, given it's case-insensitive and
linguistically aware rather than purely byte-ordinal; it agreed with the
real ordering in practice. This closes the query-side gap named above
**without needing `ADR-098`'s native-function machinery at all** — that
seam exists for a different comparison shape (decrypt-then-compare
against a plaintext bound), while ORE's own design was always meant to be
compared as opaque, already-ordered bytes.
