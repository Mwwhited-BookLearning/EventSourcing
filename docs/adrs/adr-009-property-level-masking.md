[← ADR index](../07-adrs.md)

# ADR-009: Property-level masking via a value/masked wrapper, applied only to query and stream responses

Status: Design Accepted; **implementation is lower priority — build after
Phases 0–6 are working** (per the user's own sequencing call), not
alongside them. This is a different reason for coming later than `ADR-007`:
there are no unresolved technical questions here (unlike `ADR-007`'s
open questions about join semantics), the design below is complete — it's
purely a priority/sequencing decision, not a "not sure how to build this
yet" one. Depends on `ADR-008` existing first regardless — masking only
matters for callers who already cleared the event-type-level
`RequiredReadClaim` (or the type has none) and are now looking at
individual fields within an event they otherwise have base access to.

Context: `RequiredReadClaim` (`ADR-008`) is all-or-nothing for an event
type: a caller either sees the whole event or none of it. There's a
further want for **field-level** redaction: some properties within an
otherwise-visible event should be hidden from callers who lack a
finer-grained claim — e.g. an `OrderPlaced` event's `Amount` might be
visible to everyone with `RequiredReadClaim` for the type, but a
`CustomerTaxId` property on it might need its own `pii:view` claim, and be
hidden from everyone else.

~~**Explicitly settled alongside this: there is no erasure/deletion
mechanism, and none is wanted.** A regulated field (`regulatoryClassification`,
`ADR-009` below) that some caller must never see is handled entirely by
masking it at read time — the store persists it exactly as published,
forever, same as everything else (`ADR-004`, `ADR-005`'s append-only
design). If a real deletion requirement ever surfaces (e.g. a legal
erasure order for specific data), that is a deliberately unsolved,
separate problem — not something this design silently precludes, but not
something it builds for either, since it was asked for and confirmed not
needed here.~~ **Superseded by `ADR-057`**: erasure *is* now a real
requirement, solved via crypto-shredding (per-entity data-encryption
keys, destroyed on request) rather than ever deleting/mutating a stored
event — `StoredEvent.Payload` is still never rewritten; `ADR-057`
encrypts classified fields' *values* before they're first written, so
"erasure" is destroying the key that makes existing ciphertext readable,
not touching the row at all. The wrapper below gains a third branch
(`erased`) for this — see `ADR-057`.

An earlier version of this ADR tried to solve
this by replacing the value with `null`, but that only works for
properties whose declared type already permits `null` — it doesn't "work
on all fields," which is a real requirement, not a nice-to-have.

Decision:
- Masking rules are declared per property, inside the registered
  `JsonSchema` document itself as a vendor extension:
  `"CustomerTaxId": { "type": "string", "x-masking": { "requiredClaim":
  "pii:view", "strategy": "FixedValue", "maskedValue": "***" } }` — not as
  a new column on `EventTypeDefinition` or `FilterableField`. This reuses
  `ADR-008`'s `"type:value"` required-claim string at a finer grain,
  deliberately, so the two features share one claim-checking primitive.
- **A maskable property's effective type, for any query or stream response
  (never for publish), becomes a wrapper**:
  `oneOf: [{type:"object", properties:{value: <the property's own
  declared type>}, required:["value"], additionalProperties:false},
  {type:"object", properties:{masked:{type:"string"}},
  required:["masked"], additionalProperties:false}]` — **now a three-way
  `oneOf` per `ADR-057`**, which adds `{type:"object",
  properties:{erased:{const:true}}, required:["erased"],
  additionalProperties:false}` for a field whose crypto-shredding key has
  been destroyed; not detailed further here, see `ADR-057`. A caller who holds
  `requiredClaim` sees `{"value": <the real value>}`; a caller who doesn't
  sees `{"masked": "***"}` (or whatever `maskedValue` was configured,
  defaulting to `"***"`). **This is what resolves "works on all fields":**
  the wrapper is a new type at that JSON position, so it can hold any
  original type inside `value` while `masked` is always a plain string —
  there's no longer a constraint on the property's own declared type, and
  the earlier `null`-compatibility requirement is gone entirely.
- **The wrapper shape is uniform regardless of the caller's claims** — every
  caller sees the same `oneOf(value|masked)` structure; only which branch
  is populated differs. This keeps the wire contract stable and
  independently documentable in AsyncAPI (`03-api-contracts.md`), rather
  than a shape that structurally differs per caller.
- **Two content strategies now, `"FixedValue"` and `"PartialReveal"`**
  (promoted out of the "Future" proposal below, per direct request with
  a concrete driving example — an SSN-shaped field rendered
  `XXX-XX-1234`, not just a placeholder). `"FixedValue"`: a configured
  literal string, `maskedValue`, defaulting to `"***"`.
  `"PartialReveal"`: **named, human-readable fields, deliberately not a
  cryptic mask-template string** — `{ "showFirst": 0, "showLast": 4,
  "maskChar": "X", "preserveSeparators": true }`, modeled directly on
  [PCI-DSS Requirement 3.3](https://www.strac.io/blog/pci-masking-requirements-credit-card)'s
  own plain-language framing for card PAN masking ("only the first six
  and last four digits displayed") rather than a symbolic code table
  like `System.ComponentModel.MaskedTextProvider`'s `0`/`9`/`#`/`L`/`?`
  mask syntax (a real, official .NET convention — considered and
  rejected here specifically for being less readable, not for lacking
  precedent). `showFirst`/`showLast` count real characters to reveal
  from each end; everything between is replaced with `maskChar`;
  `preserveSeparators: true` keeps literal non-alphanumeric characters
  (e.g. `-` in `123-45-6789`) showing through untouched, masking only
  the alphanumeric positions. Format-preserving, only meaningful for an
  originally-string property.
- **A third strategy, `"Hash"`, also promoted out of the "Future"
  proposal below**: `masked` carries a *keyed* HMAC of the real value
  (`{ "strategy": "Hash", "keyId": "..." }`), not a bare unsalted hash —
  a caller lacking the claim can tell two masked events share the same
  underlying value (correlation), without ever learning the value
  itself, and without the small-value-space brute-force weakness a
  plain hash would have (a bare SHA-256 of a 9-digit SSN is trivially
  reversible by precomputing all 10⁹ possibilities; a keyed HMAC is
  not). **Reuses `ADR-050`'s already-adopted `Microsoft.Extensions.
  Compliance.Redaction` `HmacRedactor`** for the actual computation —
  the same primitive, not a second hashing mechanism invented here.
  `keyId` identifies which configured HMAC key was used (supporting
  key rotation without breaking correlation for values hashed under an
  older key) — the key material itself lives in configuration/secrets,
  the same way this design already keeps every other cryptographic key
  out of the registry/schema itself.
- All three strategies (`FixedValue`, `PartialReveal`, `Hash`) fit the
  existing `oneOf` wrapper unchanged — only the *content* of `masked`
  differs, never the shape. Registering any other `strategy` value is
  rejected (`400`) — see "Future: definable masking strategies" below
  for what's still just proposed.
- **The strategies are an explicit Strategy-pattern seam
  ([Gamma, Helm, Johnson, Vlissides — *Design Patterns: Elements of
  Reusable Object-Oriented Software*, 1994](https://en.wikipedia.org/wiki/Design_Patterns),
  the Strategy pattern), not a hardcoded `switch` inside `IPayloadMasker`**
  — an `IMaskingStrategy` interface, one small class per strategy
  (`FixedValueMaskingStrategy`, `PartialRevealMaskingStrategy`,
  `HashMaskingStrategy`), each registered under its `strategy` string as
  a **keyed DI service** (`services.AddKeyedSingleton<IMaskingStrategy,
  FixedValueMaskingStrategy>("FixedValue")`, one line per strategy in the
  explicit composition root — `ADR-041`; .NET's built-in keyed-service
  support, not a bespoke registry). `IPayloadMasker` never branches on the
  strategy name itself; it resolves the matching `IMaskingStrategy` for
  each masked leaf and calls it. **This is the actual point of the
  request this formalizes**: a future fourth strategy (generalization/
  bucketing, or anything project-specific) is a new class plus one
  registration line — no change to `IPayloadMasker`'s recursion or to any
  existing strategy's code. See `06-solution-structure.md` for the
  concrete interface and registration shape, and
  [`docs/patterns/strategy-pattern-extensible-masking.md`](../patterns/strategy-pattern-extensible-masking.md)
  for the pattern itself, portably.
- **Reveal-on-demand display masking (`revealOnDemand`) — a *different*
  axis from everything above, disambiguated explicitly rather than
  conflated**: everything above answers "should this caller receive the
  real value at all" (a claims/authorization boundary). This answers a
  different question — "even a fully authorized caller shouldn't have
  the real value sitting on-screen (or in client memory) by default,
  because someone else might be reading over their shoulder." The
  well-established UX precedent is the password-field reveal toggle (an
  eye icon, WCAG-accessible, never stealing focus) — generalized here to
  any classified field, not just passwords.
  - **A `revealOnDemand`-configured field's ordinary query/stream
    response is *always* the masked/display representation — for every
    caller, including one who holds `requiredClaim`.** There is no
    claim-based branch at initial serialization time for these fields;
    simpler than the general case, not a special case of it, per direct
    observation this session. `displayMask` is computed the same way
    `PartialRevealMaskingStrategy` already computes its output
    (`showFirst`/`showLast`/`maskChar`/`preserveSeparators`, configured
    under the field's own `revealOnDemand` object — independent of
    whatever `strategy` a non-claim-holder would otherwise see, since a
    field can be `FixedValue`-masked for unauthorized callers and still
    want a partial-reveal *display* shape for this purpose).
  - **Seeing the real value is a separate, explicit action** — a small,
    dedicated GraphQL operation (`revealField(entityId, eventId,
    fieldPath)`, `ADR-037`'s transport) that checks `requiredClaim` (and,
    if configured, `ADR-066`'s step-up authentication — a field can
    require a *fresh* re-authentication specifically to reveal it, not
    just an ordinary claim) **at the moment of the request**, not at the
    time of the original bulk query. Triggered by the client's reveal
    toggle on click, not by data the client already silently received.
  - **Why this is better than sending both representations up front**,
    per direct observation this session: serialization for the common
    case (most fields, most of the time, never actually revealed) stays
    uniformly masked with no per-caller branch to compute; every reveal
    becomes its own field-granularity, timestamped entry in `ADR-045`'s
    read access audit log (sharper than knowing a bulk response merely
    *contained* a value the caller may never have looked at); and the
    real value is never present on a device until the exact moment it's
    asked for, meaningfully narrowing `ADR-065`'s local-cache exposure
    window for anything marked `revealOnDemand` specifically.
  - **The general (non-`revealOnDemand`) masking wrapper is unchanged**
    — a field without `revealOnDemand` still returns `{"value": ...}`
    directly to a claim-holder in the same response, no extra round
    trip, exactly as originally decided. `revealOnDemand` is an opt-in
    per-field trade (a request per reveal, in exchange for the real
    value never sitting in a bulk response) for fields where
    shoulder-surfing is a specific, stated concern — not a new default
    for every classified field, which would cost every bulk-authorized
    read (a full chart review, say) one round trip per field for no
    reason most fields don't need.
- `x-masking` also carries three **optional, schema-only descriptive
  fields**: `regulatoryClassification` (e.g. `"PHI"`, `"PCI"`),
  `governanceBody` (e.g. `"HHS/OCR"`, `"PCI SSC"`), and
  `regulationReference` (e.g. `"HIPAA 45 CFR §164.514(b)"`). These carry no
  enforcement behavior whatsoever — `IPayloadMasker` never reads them, and
  they never appear inside the runtime `{value:...}`/`{masked:...}`
  wrapper. They exist purely so *why* a field is masked, and under what
  regulation, is captured once at the schema and discoverable via the
  registry and generated specs — not re-derived or documented separately
  from the thing it describes.
- **Recursion through arrays** — `x-masking` is a schema-node-level
  annotation, walkable anywhere in the schema tree, and the same wrapping
  rule applies wherever it's found:
  - On a scalar property (string/number/integer/boolean): wraps that
    property's value directly, as above.
  - On an array's `items` schema, when `items` is itself scalar: wraps
    **each element** of the array — the array itself stays a plain JSON
    array, just of wrapper objects instead of bare scalars.
  - On a property nested inside an array's `items` schema, when `items` is
    a complex object (multiple properties): wraps **only that property**,
    per array element — the rest of each object in the array is untouched,
    at whatever nesting depth the recursive walk reaches it.
  - `x-masking` is **not** valid directly on a property whose own declared
    type is `object` or `array` (masking a whole nested object or an
    entire array as one collapsed unit is out of scope for v1) —
    registration rejects (`400`) that placement. It's only valid on a
    scalar node, or on an array's `items` when that `items` schema is
    itself scalar.
- **Enforcement point**: any query or event-stream response that
  serializes `Payload` back to a caller — today that's exclusively the
  Follow SSE stream (`03-api-contracts.md`); the Lineage API never
  includes `Payload` at all, so it's unaffected. If a future direct
  "read event by id" endpoint is added, masking must apply there too, or
  it's a bypass. **Publish is never affected**: a publisher always sends,
  and the store always validates/persists, the plain unwrapped value —
  `StoredEvent.Payload` is never wrapped, mutated, or touched by this
  feature at all. The wrapper exists purely at the read/response
  boundary, computed fresh from the one authoritative stored `Payload` for
  whichever caller is asking.
- The claims used are fixed for the lifetime of one Follow connection
  (same JWT throughout), so the *set* of properties (and, per the
  recursive rule, array positions) a given connection will mask is
  computed once at connect time, alongside the `RequiredReadClaim` check;
  only "is this property present in *this* event's payload" varies per
  streamed event.
- **The transform itself is a pure function of the extended `JsonSchema`
  (the one with `x-masking` annotations) and the current payload data** —
  nothing else. It does not need a `ClaimsPrincipal`, `HttpContext`, or any
  I/O; claim-checking is a separate, injected `Func<string, bool> hasClaim`
  delegate, not something the transform resolves itself. That's what makes
  it usable as a lifecycle step — a small middleware or command-chain link
  — rather than logic embedded in `FollowEndpoint` specifically: whatever
  future endpoint also serializes `Payload` can drop the same step into its
  own pipeline with zero changes to the transform. See
  `06-solution-structure.md` for the concrete shape.

Consequences:
- This is a genuine improvement over the `null`-out approach it replaces:
  masking now works on **any** scalar-typed field, including required,
  non-nullable ones, with no constraint pushed back onto schema authors.
- It also incidentally fixes a problem the `null`-out approach had: a
  masked value and a legitimately absent/`null` one are no longer
  indistinguishable. A caller can always tell which branch of the wrapper
  is populated (`value` present vs. `masked` present) — no separate
  `maskedFields` signal is needed to know a field was masked.
- The wrapper changes the *shape* every consumer of a maskable field must
  code against — `{value:...}` / `{masked:...}` instead of the bare value
  — for every caller, not just restricted ones. That's a real integration
  cost for consumers of an event type with any maskable field, accepted in
  exchange for a uniform, always-documentable wire contract and universal
  type coverage.
- AsyncAPI must document the wrapped shape as the property's real wire
  type (`03-api-contracts.md`) — the *registered* schema (what
  `SchemaValidationService` validates publish payloads against) keeps the
  plain, unwrapped type; only the generated Follow-side/AsyncAPI view
  wraps it. These are now two different views of the same property's
  type, deliberately, and `AsyncApiDocumentBuilder` is responsible for the
  transform — see `06-solution-structure.md`.
- Ordering with `ADR-008` stays fixed: the event-type-level
  `RequiredReadClaim` check happens first (all-or-nothing); masking only
  ever applies to callers who already passed that check.
- **`revealOnDemand`'s real cost is a round trip per reveal, not a
  wire-payload cost on every response** — the opposite trade-off from
  what an earlier pass through this design assumed, corrected once the
  "always send both" framing was reconsidered: ordinary responses are
  now uniformly cheaper (no per-caller branch, no second string riding
  along unused), and the cost lands only on the moment a caller actually
  clicks reveal, exactly when it's actually needed. `ADR-039`'s MVVM
  client gains one reusable "reveal toggle" component (an eye-icon
  control that calls `revealField` on click and renders the result,
  falling back to `displayMask` on any failure/denial, WCAG-accessible
  per the password-field precedent this pattern generalizes) rather than
  a bespoke control per field. `ADR-050`'s guarantee that `x-masking`
  survives into generated OpenAPI/AsyncAPI docs extends unchanged to
  `revealOnDemand` — no special-casing needed there, it's just one more
  property on the same extension object.
- `regulatoryClassification`/`governanceBody`/`regulationReference` are
  free text in v1 — validated only for "non-empty string if present," not
  against a controlled vocabulary. That's a deliberate scope decision, not
  an oversight: a fixed enum of classifications would need someone to
  decide the list, which isn't asked for here. Revisit if compliance
  tooling ever needs to query/aggregate by classification reliably (free
  text invites drift like `"PHI"` vs. `"phi"` vs. `"Protected Health Info"`
  meaning the same thing). **One reserved exception to "free text, no
  behavior"**: `regulatoryClassification: "PCI-SAD"` is checked and
  enforced, not just documented — see `ADR-071` for why masking (this
  ADR) and crypto-shredding (`ADR-057`) both fall short of PCI-DSS's
  actual requirement for that specific data class, and why registration
  itself has to refuse it instead.
- **Consumer guidance: masked/absent fields must be skipped, never
  overlaid, when building a projection.** A consumer that maintains its
  own materialized state by applying incoming event fields onto existing
  records (a read-model/projection built from the Follow stream) must
  treat a field that arrives as `{"masked": "***"}` — or is legitimately
  absent from the payload — as **no information provided**, not as an
  instruction to write `"***"`, `null`, or the wrapper object itself over
  whatever value it already has for that field. Only a `{"value": ...}`
  branch (or, for a non-maskable field, its plain value) should ever
  update a consumer's own state. This is guidance for consumers, not
  something the store enforces or can verify — the store has no
  visibility into a downstream consumer's state to overlay onto in the
  first place (`Payload` itself is append-only and never mutated). Getting
  this wrong would let a caller who *temporarily* loses the claim (or
  simply reprocesses history from an earlier connection with fewer
  claims) silently clobber good previously-known data in their own
  projection with a placeholder — exactly the kind of corruption masking
  exists to prevent, just one layer further downstream than the store
  itself can reach.

## Surfaced in generated docs, and reused for log redaction (`ADR-050`)

Two extensions on top of the Decision above, added by `ADR-050`, not a
revision of it: `x-masking` (already declared here) is now guaranteed
to survive into the generated OpenAPI/AsyncAPI documents as a real
Specification Extension, not just tracked internally; and the same
`regulatoryClassification`/`requiredClaim` metadata this ADR already
carries is reused to drive `Microsoft.Extensions.Compliance.Redaction`-
based log redaction — a second, additive enforcement surface (logs),
never touching what `StoredEvent.Payload` actually persists, same as
every enforcement point this ADR already describes.

## Future: declined masking strategies (KISS — not scheduled)

`"FixedValue"`, `"PartialReveal"`, and `"Hash"` are all decided and
built now (see the Decision above). Format-preserving encryption,
generalization/bucketing, and tokenization were compared in
`docs/comparisons/masking-strategies.md` and are **explicitly declined**,
applying KISS (prefer the simplest design that satisfies a *stated*
requirement over building speculative capability for one that hasn't
shown up):

- Tokenization (a separate-party, separate-mechanism reversal model)
  explicitly does **not** fit the `oneOf` wrapper the way the three built
  strategies do, and would need its own mechanism entirely if ever
  built — not a fourth `strategy` value. The clearest decline of the
  three: no stated need for "someone other than the reader reverses this
  later," and this isn't where it would be built even if there were.
- Generalization/bucketing *would* fit as an ordinary fourth
  `IMaskingStrategy` implementation (see the Decision's Strategy-pattern
  seam above) if it's ever decided — declined for now anyway, since a
  fourth strategy plus the documentation discipline of never overclaiming
  k-anonymity is real added surface for no stated need.
- Format-preserving encryption (real FF1/FF3-1, not `PartialReveal`'s
  simpler reveal-and-mask) is declined because it would be this design's
  first cryptographic key-management surface — `Hash` avoided that cost
  by reusing `HmacRedactor`'s existing keying; FPE has no equivalent
  existing primitive to reuse.
- Whole-object or whole-array masking (collapsing an entire nested object
  or array into one `{value:...}`/`{masked:...}` at that position, instead
  of recursing into it) is explicitly out of scope for v1 (see the
  "not valid directly on `object`/`array`" rule above) — a candidate to
  revisit if a real need shows up, same as the three above.
- None of this is scheduled, and none is expected — it's recorded so
  `"FixedValue"`/`"PartialReveal"`/`"Hash"` don't get treated as
  permanently incomplete by accident. Revisit only if a concrete
  requirement for one of these shows up; see
  `docs/comparisons/masking-strategies.md` for the full reasoning.
