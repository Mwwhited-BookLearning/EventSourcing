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

**Explicitly settled alongside this: there is no erasure/deletion
mechanism, and none is wanted.** A regulated field (`regulatoryClassification`,
`ADR-009` below) that some caller must never see is handled entirely by
masking it at read time — the store persists it exactly as published,
forever, same as everything else (`ADR-004`, `ADR-005`'s append-only
design). If a real deletion requirement ever surfaces (e.g. a legal
erasure order for specific data), that is a deliberately unsolved,
separate problem — not something this design silently precludes, but not
something it builds for either, since it was asked for and confirmed not
needed here.

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
  required:["masked"], additionalProperties:false}]`. A caller who holds
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
- **v1 has exactly one content strategy, `"FixedValue"`** (a configured
  literal string, `maskedValue`, defaulting to `"***"`). Registering any
  other `strategy` value is rejected (`400`) — see "Future: definable
  masking strategies" below for what else this is expected to grow into.
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
- `regulatoryClassification`/`governanceBody`/`regulationReference` are
  free text in v1 — validated only for "non-empty string if present," not
  against a controlled vocabulary. That's a deliberate scope decision, not
  an oversight: a fixed enum of classifications would need someone to
  decide the list, which isn't asked for here. Revisit if compliance
  tooling ever needs to query/aggregate by classification reliably (free
  text invites drift like `"PHI"` vs. `"phi"` vs. `"Protected Health Info"`
  meaning the same thing).
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

## Future: definable masking strategies (proposal, not decided)

`"FixedValue"` is the only strategy built now. This section stays as a
proposal for later, kept explicitly separate from the Decision above so
it's unambiguous what's built versus sketched:

- Widen `x-masking.strategy` beyond `"FixedValue"` to e.g. `"PartialReveal"`
  (keep the last N characters of the real value inside `masked` — only
  meaningful for an originally-string property) or `"Hash"` (a
  deterministic hash of the real value inside `masked`, letting a caller
  correlate masked values across events without ever seeing the
  underlying value). Both still fit the existing wrapper — only the
  content of `masked` changes, never the shape — so this is a smaller
  extension than it would have been under the old null-out design.
- Whole-object or whole-array masking (collapsing an entire nested object
  or array into one `{value:...}`/`{masked:...}` at that position, instead
  of recursing into it) is explicitly out of scope for v1 (see the
  "not valid directly on `object`/`array`" rule above) — a candidate for
  this same future pass if a real need shows up.
- None of this is scheduled; it's recorded so `"FixedValue"`-only v1
  doesn't get treated as the final word by accident.
