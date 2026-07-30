[← ADR index](../07-adrs.md)

# ADR-018: Event upcasting for schema evolution

Status: Accepted

Context: `EventTypeDefinition` already supports multiple schema versions
(`02-data-model.md`), and `StoredEvent.SchemaVersion` records which
version validated a given event at publish time (`ADR-011`'s
consequences). But nothing in the design so far reshapes an old-version
payload into the current version's shape for a consumer. `ADR-010`'s
`mode=replay` makes this a concrete problem, not a hypothetical one:
replaying an event type's full history from `fromSequenceNumber=0` can
burst events spanning every schema version that type has ever had, in one
stream — and a consumer, especially a CQRS projection
(`09-cqrs-read-models.md`) whose `Project` function expects one consistent
shape, has no designed way to reconcile that today.

A note on terminology before the Decision: this ADR's transform is
sometimes called a "projection" in the wider industry (mapping one shape
to another) — that word is avoided here entirely, because `ADR-015`/
`ADR-016` already give it a specific, different meaning in this design
(a CQRS read model). Everywhere below, "upcast mapping" or "`compute()`
expression" is used instead, deliberately.

**Revised in place, this session, after a design review found this ADR
was never actually updated despite `ADR-037` explicitly saying it needed
to be** ("`ADR-018`'s upcast mechanism moves off OData `compute()`...
`ADR-018` itself needs revising — flagged here, not yet propagated") and
`CLAUDE.md` incorrectly asserting it had been. Per this project's
additive-history convention, only the OData-specific mechanism below is
struck through — the cardinality reasoning (why a narrow expression-list
shape, not a general transform language, is enough) is unaffected and
kept as-is, since `ADR-037`/`ADR-053` never revisited *that* question,
only *which engine* evaluates the expression.

Decision:
- Each registered schema version `>= 2` gets an optional
  `upcastFromPrevious` field: ~~an **OData `$apply` `compute()`
  expression list** (`<expression> as <alias>`, comma-separated — OASIS
  OData v4.01 Data Aggregation extension, `docs/references.md`)~~
  **superseded by `ADR-037`/`ADR-053`**: a **CEL (Common Expression
  Language) expression list by default**, evaluated by the pluggable
  `IUpcastExpressionEvaluator` seam (`ADR-053`) — JSONata is a named
  alternative a deployment can register instead. Reused once OData was
  removed from this design's API surface entirely (`ADR-037`), which
  made `Microsoft.OData.UriParser` no longer a dependency this design
  carries for any reason. — **not** a general-purpose transform language
  like JSONata by default. This travels through
  the same `PUT /registry/{event-type}` call as everything else — it's
  data, registered live, not a code deployment.
- **Why a narrow, `compute()`-shaped expression list is enough, and
  deliberately not more — unaffected by the OData→CEL change above**:
  an upcast mapping is structurally always **many-source-fields-to-one-
  destination-field, or one-to-one** — combine or rename/recompute
  sibling fields of the *same* stored event into the new version's
  fields. It is never one-to-many (one source field fanning out into
  several destination fields) and never many-to-many (a join or an
  array-cardinality change) — an upcast reshapes one event's own
  payload into one new-version payload, it doesn't join across events
  or restructure array shape. A flat "expression `as` alias" list
  already matches that cardinality exactly, whether evaluated by CEL or
  the OData parser this design no longer uses; none of JSONata's
  object/array-construction or looping constructs would ever be
  exercised, so adopting a full transform language for this would be
  strictly more power than the problem has.
- ~~**Why OData specifically, over JSONata or a bespoke format**: this
  design already depends on `Microsoft.OData.UriParser` for `$filter`
  (`04-odata-filter-pushdown.md`)...~~ **Superseded by `ADR-037`** — OData
  is gone from this design's API surface entirely; see `ADR-053` for why
  CEL is the new default (safety/performance/problem-fit for the common
  case) and why the engine is kept pluggable rather than a second
  permanent lock-in.
- Each expression's alias must name an actual property of the *destination*
  version's schema — validated at registration (`05-schema-registry-and-
  spec-generation.md`), alongside the existing structural checks. An
  alias that doesn't correspond to any destination property, or an
  expression that fails to parse, is rejected `400` at registration time
  — the first concrete piece of registration-time compatibility checking
  this design has (see Consequences).
- `UpcastChain` (`06-solution-structure.md`) is a single generic executor,
  not `N` hand-written classes: for each version hop between a
  `StoredEvent`'s `SchemaVersion` and the event type's current active
  version, it retrieves that version's `upcastFromPrevious` clause,
  evaluates each `expression as alias` against the previous hop's fields
  via the registered `IUpcastExpressionEvaluator` (`ADR-053`; CEL by
  default, not a bespoke interpreter), and assembles the next hop's
  payload from the results.
  Applied before the payload reaches a consumer — Follow and any CQRS
  projection (`ProjectionHost`, `ADR-015`). Lineage never includes
  `Payload` at all (`ADR-009`), so it's unaffected.
- `StoredEvent.Payload` is never rewritten — upcasting is a read-time
  transform, computed fresh per response, the same non-destructive posture
  already taken for masking (`ADR-009`) and for never deleting/mutating
  stored data (`ADR-009`'s closing note).
- Registering a new schema version still does **not** require an
  `upcastFromPrevious` clause — a purely additive-optional-field change
  may need no transform at all; that hop's payload simply passes through
  unchanged.

Consequences:
- Follow/`ProjectionHost` consumers, across a `mode=replay` burst spanning
  many schema versions, now see one consistent (current-version) shape
  throughout, instead of branching on `SchemaVersion` themselves — the
  direct fix for the gap in Context.
- Evaluating an upcast expression runs per event, on every read — for a
  high-volume replay this is a real, uncached cost; no upcast-result
  caching is designed here, the same category of accepted v1 cost as
  Follow's unbounded replay burst (`ADR-010`).
- No second expression grammar beyond whatever `IUpcastExpressionEvaluator`
  backend is registered (`ADR-053`) for maintainers to learn — the
  direct trade for a narrow expression-list shape's reduced expressiveness
  versus a general transform language.
- ~~The OASIS Data Aggregation extension's `compute()` grammar has no
  confirmed native default-if-missing/coalesce function...~~ **Moot,
  OData removed (`ADR-037`)** — CEL's own conditional/ternary operators
  cover "use `USD` if `Currency` is absent" natively; verify against
  whichever CEL implementation `ADR-053` pins, not assumed.
- **This narrows, but does not close,** registration-time compatibility
  checking on its own — a syntactically-broken or misaliased
  `upcastFromPrevious` clause is caught at `400`, but whether an
  expression's *output* actually validates against the destination schema
  is never checked there. `ADR-020` is what actually closes this, by
  running the real thing against real data at publish time instead of
  needing synthetic representative data here.
- **A hard boundary, not a currently-needed capability**: if a future
  event type ever genuinely needs a one-to-many or many-to-many reshape
  across a version bump, `compute()` stops being sufficient and a general
  transform language (JSONata — see `docs/references.md` — or similar)
  would need revisiting. Nothing observed in this design's event types so
  far needs that; recorded here so it isn't rediscovered from scratch.
