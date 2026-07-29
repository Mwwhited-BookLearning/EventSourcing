[← Comparisons index](README.md)

# Upcast Transform Language: CEL vs. JSONata (vs. JMESPath, JOLT, OData `compute()`)

**Decided in:** `ADR-018` (originally OData `compute()`), superseded by
`ADR-037` (JS/CEL — Jint for the rare complex case, CEL for the common
declarative one). **Written here:** the dedicated side-by-side
`docs/comparisons/README.md` already flagged as missing — CEL and
JSONata get the deepest treatment, per direct request; `compute()`,
JMESPath, and JOLT are covered at the depth `references.md` already
established, not re-derived.

**Stated requirement driving this comparison:** an upcast mapping is
always many-source-fields-to-one-destination-field or one-to-one —
**never** a restructuring/fan-out (one-to-many or many-to-many) —
evaluated potentially very frequently (every read of an old-version
event, `mode=replay` bursts spanning years of schema changes,
`ADR-018`), and the expression itself is registered data an operator
supplies, not a compile-time constant — it needs to run safely against
untrusted-ish, versioned mapping expressions.

## The options

### Option A — CEL (chosen, common declarative case)

| | |
|---|---|
| **Pros** | Purpose-built for exactly this shape: [cel.dev](https://cel.dev/) describes it as "fast, portable, and safe to execute," **non-Turing-complete by design** (no loops that don't terminate, no unbounded recursion), evaluating in nanoseconds-to-microseconds — built for "predicates and simple data transformations," which is precisely what an upcast mapping is and never more than that. Used in production for exactly this kind of embedded, frequently-re-evaluated, operator-authored expression (Kubernetes admission policies, Envoy, Google Cloud IAM conditions, Firebase security rules). |
| **Cons** | **The .NET port ecosystem is genuinely fragmented** — four small, differently-maintained community packages (`Cel.NET`, `Cel`, `cel-net`, `cel-csharp`), no single dominant implementation the way there is for JS/Go/Java (`docs/libraries/dotnet/cel-dotnet.md`'s own honest caveat). The *design fit* is excellent; the *concrete .NET implementation maturity* is the weakest part of this option, not the strongest. |

### Option B — JSONata

| | |
|---|---|
| **Pros** | The most "universal" option in the original shortlist (independent implementations across JS/Java/Python/Go/C++/.NET/Rust) and a genuine fit for "path expressions with functional descriptions." **The .NET port specifically is the more mature, more consolidated choice right now**: [`Jsonata.Net.Native`](https://www.nuget.org/packages/Jsonata.Net.Native/) is a single, actively-maintained package (reached v3.0.0), validated against the official JSONata JS test suite for spec conformance, zero external dependencies — a meaningfully stronger position than CEL's four-way-split .NET landscape. |
| **Cons** | Strictly more general than the problem: full object/array construction, functions, joins, and looping constructs an upcast mapping (one-to-one/many-to-one only) would never exercise — the exact "more general than the problem" reasoning `references.md` already recorded against it. Not designed around bounded, predictable-cost evaluation the way CEL explicitly is — a richer, heavier evaluator for a narrower job. |

### Option C — JMESPath

| | |
|---|---|
| **Pros** | Real, multi-language (used across every AWS SDK), one notch narrower than JSONata (no user-defined functions/closures) — closer to the problem's actual shape. |
| **Cons** | Same "more general than strictly needed" concern as JSONata, just less pronounced; no meaningfully stronger .NET-specific implementation story than either CEL or JSONata to offset that. |

### Option D — JOLT

| | |
|---|---|
| **Pros** | Purpose-built for JSON-to-JSON structural transforms. |
| **Cons** | Java-only, no cross-language ports at all — ruled out outright for a .NET-first design, independent of any other consideration. Also structural-only by its own docs ("write code to fix values" for anything computed) — fails the "functional descriptions" requirement this problem actually has. |

### Option E — OData `compute()` (the original `ADR-018` choice, superseded)

| | |
|---|---|
| **Pros** | Reused `Microsoft.OData.UriParser`, already a dependency for `$filter` at the time — "prefer reusing an existing primitive over adding a new dependency," a real, valid argument *then*. |
| **Cons** | That reuse argument stopped holding the moment OData was swapped out for GraphQL (`ADR-037`) — the parser it reused no longer exists in this design at all. Superseded, not merely deprioritized. |

## Worked example — the same mapping in both languages

A `v1` `OrderPlaced` event carries separate `FirstName`/`LastName`
fields; `v2` combines them into one `CustomerName` field — many-to-one,
exactly the shape `ADR-018` restricts upcast mappings to.

| | Expression |
|---|---|
| **CEL** | `event.FirstName + " " + event.LastName` |
| **JSONata** | `FirstName & " " & LastName` |

**A real, worth-knowing syntax trap**: JSONata reserves `+` for numeric
addition only and uses `&` for string concatenation — writing
`FirstName + LastName` by habit (coming from CEL, JS, or C#, where `+`
overloads onto strings) throws a type error in JSONata instead of
concatenating.

A second case — coercing a legacy, inconsistently-typed `Amount` field
(stored as a string in old data) to a number:

| | Expression |
|---|---|
| **CEL** | `double(event.Amount)` |
| **JSONata** | `$number(Amount)` |

## Three more, moving from simple to genuinely revealing

**Tiered/conditional derivation** — `v2` needs a `PriorityTier` computed
from `Amount`:

| | Expression |
|---|---|
| **CEL** | `event.Amount > 10000 ? "Platinum" : event.Amount > 1000 ? "Gold" : "Standard"` |
| **JSONata** | `Amount > 10000 ? "Platinum" : Amount > 1000 ? "Gold" : "Standard"` |

Nearly identical — both support chained ternaries.

**Optional field, null-coalescing** — `v1`'s `Address2` may be absent;
`v2` wants it folded into `FullAddress` only if present:

| | Expression |
|---|---|
| **CEL** | `event.Address1 + (has(event.Address2) ? " " + event.Address2 : "") + " " + event.City` |
| **JSONata** | `Address1 & ($exists(Address2) ? " " & Address2 : "") & " " & City` |

`has()` (CEL) and `$exists()` (JSONata) are each language's field-presence
macro — same idea, different name.

**Array aggregation — the one that actually reveals a real capability
gap.** `v1` has `LineItems: [{Amount: number}, ...]`; `v2` collapses the
whole array into a single `TotalAmount` (many-to-one, from array elements
rather than sibling fields — still within `ADR-018`'s allowed shape):

| | Expression |
|---|---|
| **JSONata** | `$sum(LineItems.Amount)` |
| **CEL** | *No standard equivalent* — verified against the [CEL language spec](https://github.com/google/cel-spec/blob/master/doc/langdef.md): the only standard macros are `has()`, `all()`, `exists()`, `exists_one()`, `map()`, `filter()` — no built-in reduce/sum. Summing would need a custom function registered in the hosting C# environment, not "just CEL" anymore. |

JSONata's path evaluation naturally flattens `LineItems.Amount` into a
sequence across the array, and `$sum()` is a built-in reducer over it.
CEL's minimal, deliberately-safety-first macro set has no equivalent —
a real cost to weigh if this project ever needs an array-aggregation
upcast, not just field-level ones, alongside the .NET-maturity tension
already recorded above.

## Recommendation

**CEL remains the better fit on the axes that matter most for the
common case — safety, performance, and narrowness matched to the
typical field-level upcast shape** — and stays this design's pick
(`ADR-037`) for that case, unchanged by this comparison. Two honest
complications this comparison surfaces, neither glossed over:

- **On .NET implementation maturity, JSONata is currently ahead** — one
  consolidated, spec-conformant, actively-maintained package vs. CEL's
  four fragmented ones.
- **On array-aggregation mappings specifically (worked example
  above), CEL has no native answer at all** — this isn't a performance
  or maturity gap, it's a real expressiveness gap: base CEL's macro set
  has no reduce/sum, full stop, where JSONata's does natively. If this
  project's real upcast needs turn out to be overwhelmingly field-level
  (rename/concatenate/coerce/conditional), CEL's gap here never gets
  exercised; if a real array-aggregation upcast shows up, CEL would need
  a custom host-registered function to cover it — a real added cost
  JSONata wouldn't have.

Neither complication flips the recommendation outright, but together
they mean "CEL, obviously" understates the real trade-off. Recorded as
its own row in `docs/10-open-questions.md` rather than quietly
resolved here: the right call may come down to which upcast shapes
this project actually ends up needing, not which language looks better
in the abstract.
