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

## Recommendation

**CEL remains the better fit on every design axis that matters most —
safety, performance, and narrowness matched to the actual problem
shape** — and stays this design's pick (`ADR-037`), unchanged by this
comparison. But the honest complication this comparison surfaces,
worth stating plainly rather than glossing over: **on the one axis that
determines whether this is easy to actually build in .NET today,
JSONata is currently ahead** — one consolidated, spec-conformant,
actively-maintained package vs. CEL's four fragmented ones. This is a
real tension, not a settled question — recorded as its own row in
`docs/10-open-questions.md` rather than quietly resolved here: the
right call may come down to how the CEL-for-.NET ecosystem looks at
actual build time, not how it looks today.
