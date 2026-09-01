# NRules + DMN spike — Option E

Proves the "rules/decision engines" option from
[`docs/comparisons/user-flow-dsl.md`](../../../docs/comparisons/user-flow-dsl.md):
a real `NRules` RETE rule engine driving the flow's sequencing, with the
one genuinely multi-factor decision (does this event need secondary
review?) delegated to a real DMN 1.3 decision table via
`net.adamec.lib.common.dmn.engine`.

Run with `dotnet run` from this directory.

## Two files, two different jobs

| File | Role |
|---|---|
| `Dmn/AdverseEventClassification.dmn` | A real, standalone DMN 1.3 XML decision table (never an inline C# string, per direct request) — inputs `SeverityScore`/`EventType`, output `ReviewPath` (`SecondaryReview` or `ImmediateFold`). Answers one question: how severe is this event? |
| `AdverseEventReviewRules.cs` | Eight `NRules.Fluent.Dsl.Rule` classes, each matching on facts already asserted by an earlier rule. Answers a different question: given the classification, what happens next, in what order? |

This mirrors the comparison doc's own framing of Option E: DMN and NRules
are not alternatives to each other here, they're paired for two
different jobs neither is well-suited to alone.

## How the flow actually runs — no AST, no interpreter

Every other spike in this folder (PlantUML-native, Elsa, ANTLR) parses
some source into an AST and walks it. This one has no AST at all. Each
rule fires once its own precondition facts exist:

1. `ClassifyEventRule` — fires on `AdverseEventReported`, calls the DMN
   table via `IAdverseEventClassifier` (NRules dependency injection),
   inserts `Classified`.
2. `RouteToSecondaryReviewRule` → `DelegateAccessRule` →
   `ColleagueReviewRule` → `PublishDecisionRequestRule` — each fires
   only once the previous rule's own inserted fact exists, forming a
   chain purely through fact dependencies, not explicit sequencing code.
3. `FoldOnAcceptRule` / `LeaveUntouchedOnRejectRule` — fire once an
   `AuthorityDecisionPublished` fact exists, whichever way it went.
4. `FoldImmediateRule` — fires directly off `Classified` when the DMN
   table said `ImmediateFold`, skipping the whole secondary-review chain.

`Program.cs` inserts the initial `AdverseEventReported` fact and calls
`session.Fire()` once. For the two `SecondaryReview` scenarios, the rule
chain runs rules 1–4 in that same `Fire()` call, then **stops on its
own** — nothing left to match until the PI's real decision exists.
`Program.cs` then inserts `AuthorityDecisionPublished` and calls
`Fire()` again to resume. No blocking activity, bookmark, or explicit
pause API was written for this — it's a direct consequence of forward
chaining over facts that don't exist yet, discovered by running it, not
designed in ahead of time.

## Findings

Worked end to end; all three scenarios (accepted / rejected /
non-serious) produce the same actions in the same order as every other
spike in this folder. Two real, worth-naming things found only by
running the actual package, not by reading its docs:

1. **`DmnExecutionContext.ExecuteDecision(name)` matches the DMN
   `<decision>` element's `name` attribute, not its `id`.** Calling it
   with the `id` (`"classifyAdverseEvent"`) throws `DmnExecutorException:
   decision ... not found`; the working call uses the human-readable
   `name` (`"Classify Adverse Event"`). Confirmed via a throwaway
   reflection probe against the installed assembly, the same technique
   the Elsa spike used for its own API-drift findings — the package's
   own README never states which attribute is used.
2. **A DMN `<output>` element's result key comes from its `name`
   attribute**, inherited from a `NamedElement` base type not visible on
   `DecisionTableOutput` itself without walking the inheritance chain —
   several of the DMN engine's own upstream test fixtures leave `output
   name=""` blank, which would make that output unreachable by name;
   this spike's own `.dmn` file sets `name="ReviewPath"` explicitly.
3. **`net.adamec.lib.common.dmn.engine` pulls a stale `Newtonsoft.Json
   12.0.2`** (a known high-severity CVE, `GHSA-5crp-9r3c-p9vr`)
   transitively via `DynamicExpresso.Core` — confirmed with `dotnet list
   package --include-transitive`, surfaced automatically as an `NU1903`
   build warning. A real, unresolved dependency-hygiene concern specific
   to this DMN library choice.

The genuinely useful, not-designed-in-advance finding is the first
section above: **a "wait for human input" pause point falls out of
forward-chaining for free** in a RETE engine, where Elsa (Option B)
needed a purpose-built blocking activity/bookmark mechanism to do the
same job — a real structural difference between "declarative rules
matched against accumulating facts" and "an interpreter walking a
sequential AST," worth weighing on its own merits, independent of either
option's other tradeoffs.
