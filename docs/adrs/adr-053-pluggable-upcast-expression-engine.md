[← ADR index](../07-adrs.md)

# ADR-053: Pluggable declarative upcast expression engine, defaulting to CEL

Status: Accepted

Context: `docs/comparisons/upcast-transform-language.md` found CEL the
better fit for the common field-level upcast case, but surfaced two
real, live tensions rather than a clean win: JSONata's .NET port is
currently more mature than CEL's fragmented .NET ecosystem, and CEL has
no native array-aggregation/reduce capability at all (verified against
the CEL language spec), where JSONata's `$sum()`/sequence-flattening
handles it natively. Direction received this session resolves this not
by picking one language forever, but by not needing to: make the
declarative expression engine **pluggable, swappable per deployment via
configuration**, defaulting to CEL until a real need for something more
expressive shows up.

Decision:
- **`IUpcastExpressionEvaluator` is the seam** — the core engine's
  `UpcastChain` (`ADR-018`) depends on this interface, not on CEL or
  JSONata directly, resolved through the explicit composition root
  (`ADR-041` — no reflection-based auto-selection). `EventTypeDefinition.
  UpcastFromPrevious`'s expression *string* is engine-agnostic text; which
  engine parses/evaluates it is a deployment-time configuration choice,
  not baked into the registered schema itself.
- **CEL is the default, registered implementation** — matching
  `docs/comparisons/upcast-transform-language.md`'s recommendation for
  the common case: narrower, safer, faster, purpose-built. A deployment
  that never needs array-aggregation upcasts never has a reason to
  change the default.
- **JSONata is a documented, supported alternative implementation** — a
  deployment that hits a real array-aggregation upcast need (or wants
  `Jsonata.Net.Native`'s currently-more-mature .NET package over CEL's
  fragmented one) registers it instead, via ordinary DI configuration,
  with no core-engine code change. `Jint` (`ADR-037`'s complex-case
  escape hatch) is unaffected — it remains the separate, always-available
  path for the rare case neither declarative engine covers.
- **One engine active per deployment, not mixed per event type** — the
  simplest option that satisfies the actual requirement (avoid a
  premature permanent language commitment); per-event-type engine
  selection was considered and not adopted, since nothing has asked for
  mixing two declarative engines in one deployment, and it would add
  real complexity (which engine parsed *this* registered expression,
  tracked per-registration) for a need that hasn't shown up.

Consequences:
- Resolves the open question `docs/10-open-questions.md` tracked for
  CEL vs. JSONata — no spike required before building; the spike, if
  ever needed, becomes "swap the configured implementation," not "pick
  once, irreversibly."
- `docs/libraries/dotnet/cel-dotnet.md` and `.../jsonata-dotnet.md` (not
  yet written — flagged) both become real, supported implementations of
  one interface, not a forced single choice between them.
- `06-solution-structure.md`'s DI-wiring sketches (already flagged
  stale, predating `ADR-041`) need `IUpcastExpressionEvaluator`'s
  composition-root registration shown explicitly once redone — not done
  this pass.
