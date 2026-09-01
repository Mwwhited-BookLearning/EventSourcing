[← ADR index](../07-adrs.md)

# ADR-101: PlantUML-native executable flow engine + `PendingTask` read model

Status: Accepted

Context: `docs/10-open-questions.md`'s former row 1 asked whether user
flows/approvals should move to a general-purpose DSL, or keep composing
this project's existing bespoke mechanisms (`RequiredSignature`/step-up
`ADR-066`, gated `authorityDecision` publish `ADR-042`/`ADR-023`/
`ADR-035`, `ExpectedResponse` tracking `ADR-094`). `docs/comparisons/
user-flow-dsl.md` weighs eight real options against this repo's own
stated preferences (textual, diff-friendly, PlantUML-consistent,
non-developer-readable at the source level), built and ran two of them
end to end as a head-to-head shootout (`spikes/user-flow-dsl/`), and
found — directly checked, not assumed — that **there is no procedural
flow code anywhere in the existing Vitals/Meridian workflows to
convert**: every branch/gate is already declarative schema registration
(`RequiredClaims`/`RequiredSignature`/`ChangeKind`/`ExpectedResponse`)
resolved by shared, already-tested, generic framework mechanisms
(`AuthorityDecisionResolver`, `StepUpEvaluator`, `ExpectedResponseWatcher`).
The user separately asked for a client-side task-management surface —
explicitly **"just a query... fed from events like everything else"** —
which ruled out any Temporal/Zeebe-shaped durable workflow-instance
engine outright.

Decision:
- **Adopt Option G1** from the comparison: a small, in-house interpreter
  for a constrained PlantUML Activity Diagram subset
  (`@startuml`/`start`/`stop`/`:action;`/`if (cond) then (yes) ... else
  (no) ... endif`/`@enduml`), where the `.puml` file is simultaneously
  the documentation diagram, the reviewed source, and the thing that
  actually runs — no separate translation step, no generation artifact.
  Parses via a **real ANTLR4 grammar + generated Listener**
  (`Antlr4BuildTasks` NuGet package), not a hand-rolled parser — a
  direct, later refinement of the comparison's own G1 spike, made once
  the user stated a preference for "g4 with listener and the antlr
  nuget package over a hand rolled parser." The grammar is a strict
  superset of the hand-rolled parser's own line-based subset (whitespace/
  newlines are skippable anywhere), so every finding in the comparison's
  Scorecard still holds.
- **The engine runs entirely on the read side**, as one more consumer of
  the already-established `IProjection<TReadModel>`/`ProjectionHost`
  mechanism (`ADR-015`/`ADR-016`, `docs/09-cqrs-read-models.md`) —
  `AuthorityDecisionResolver`, `PublishService`, `RouterWorker`, and
  `ExpectedResponseWatcher` are **not touched**, confirmed directly by
  reading `RouterWorker.cs`: `AuthorityDecisionResolver.ProcessAsync`
  runs inline in the write-path fold loop as a fixed reactor, not a
  designed extension seam. This makes the whole feature additive and
  low-risk — every existing integration/E2E test for the converted
  workflows, and for `OrderSummaryProjection`, keeps passing unmodified.
- **The key design property satisfying "just a query, fed from events, no
  durable instance state"**: the flow's AST is not stepped through once
  and remembered — it is **re-evaluated statelessly** against the
  entity's current merged JSON snapshot on every relevant event, exactly
  like `ProjectionHost`'s existing full-rebuild-equals-incremental-
  catch-up property already works. A `PendingTask` row exists for
  exactly as long as the AST walk currently reaches an unresolved `task`
  node for that key — no separate "flow instance" storage concept
  exists anywhere in this design.
- Three **additive, default-interface-method** extensions to
  `IProjection<TReadModel>` (`EventStore.Projections.Abstractions`),
  source- and binary-compatible for the one pre-existing implementer
  (`OrderSummaryProjection`, unaffected): an eventId-aware `GetKey`
  overload (a raiser event has no payload field to key by — it's keyed
  by its own `EventId`); `ChangeKind? OverrideChangeKind(string
  eventType)` (a resolver event like `authorityDecision` must be forced
  `Partial` for this correlation join, without touching that type's own,
  unrelated `Full` registration); and a **nullable** `Project` return
  (`null` means "no open task for this key right now," and
  `ProjectionHost` deletes any existing row rather than upserting). See
  `docs/09-cqrs-read-models.md`'s "Second worked example" section for
  the full shape and reasoning.
- **New label-text conventions**, interpreter-level only — the grammar
  itself never sees these, they're resolved by `FlowInterpreter` against
  the actions/conditions dictionaries a `FlowDefinition` supplies: a
  plain action `:label;` is looked up verbatim, unregistered ⇒ throw
  (never a silent no-op); a **task action**, recognized structurally
  before the plain-action lookup so it can never hit "unregistered" —
  `:task "<description>" claim="<claim>" resolvedBy="<EventType>[|<EventType>...]" [correlatedBy="<FieldName>"];`
  (`resolvedBy`'s `|`-list reuses this project's existing OR-of-list
  claim idiom, `ADR-050`; `correlatedBy` defaults to `targetEventId`,
  must be the payload's own PascalCase property name); a condition
  `<FieldName>?` uses one generic field-truthy rule (`mergedState
  [FieldName]` is JSON `true` or the string `"accepted"`) — anything
  else must be registered explicitly as a `Func<JsonObject, bool>`.
- All four already-built Vitals/Meridian workflows needing a human
  decision (B, D, A, C) are converted: one real `.puml` per workflow,
  embedded as a resource in the owning `Samples.*` project (so the
  executing assembly and the documentation diagram are the *same file*,
  nothing can drift), plus one small `*.Flow.cs` registering its
  `FlowDefinition` — the underlying, already-tested schema registration
  files (`VitalsWorkflowB.cs` etc.) are **not modified**.
- Client-side: one cross-domain, **polled** (not subscribed — there is
  no `myTasks` GraphQL Subscription field) `useMyTasks.ts` composable,
  `MyTasksView.vue`/`TasksView.vue`, wired into the router/nav shell
  (`ADR-099`) at `/tasks` with **no domain gate** — the query itself
  spans every domain sharing one Host's own database, matching the real
  `EventStore.AppHost` topology (`eventstore` serves both `trial1`/`kyc`
  `AppId`s from one deployment already). "Open" navigates to the
  existing `/queue` accept/reject screen — the task list only discovers
  work, it never publishes a decision itself.

Consequences:
- A repeated, real defect class was found and fixed **because** this
  feature was verified live (real browser, real DevIdp-issued tokens),
  not just via unit tests — `docs/bugs/framework/service/devidp-
  duplicate-typed-extra-claims-collapse.md` (a client seeded with more
  than one claim of the same type silently lost all but the last) and
  `docs/bugs/framework/service/devidp-token-endpoint-missing-standalone-
  cors-origin.md` (DevIdp's own token endpoint had no CORS allow-list
  entry for standalone client-web dev). Neither is specific to this
  feature — both would have eventually bitten some other caller — but
  neither had ever been exercised through a real DevIdp-issued token
  before this pass.
- `PendingTasksDbContext` is **SQLite-only, regardless of the write-side
  provider** — the same "one EF Core provider is sufficient for a read
  model" precedent `docs/09-cqrs-read-models.md` already established for
  `OrdersProjectionsDbContext`. `EventStore.Host.Sqlite` and
  `EventStore.Host.Postgres` both wire it (and automatically get
  `myTasks` via the shared `AddEventStoreGraphQl()`); `EventStore.Host.
  SqlServer` does not yet — tracked in `TODO.md`, not a design gap
  (`EventStore.AppHost`, `ADR-001`, only ever targets one provider at a
  time, currently Postgres, so nothing has forced it).
- `EventStore.AppHost` gained two new resources, `vitals-flows`/
  `meridian-flows` — the **first** CQRS projection this repo has ever
  wired into a real orchestrated AppHost run (neither this item's own
  dependency, "CQRS Read-Model Projections," nor `Samples.Orders.
  Projections` had been, before this pass). Both share one physical
  `../pending-tasks.db` file with `eventstore` itself so the
  cross-domain `myTasks` query actually sees both domains under a real
  run, not only in isolated tests.
- **Not solved here, deliberately**: a general-purpose rules-engine
  capability (multi-fact validation beyond what `JsonSchemaInstanceValidator`
  already expresses) — `docs/comparisons/user-flow-dsl.md`'s own
  Recommendation names `NRules`/DMN as a real, separate addition
  *alongside* this decision if that need ever materializes, not
  something this ADR's narrower scope (single-branch human-decision
  flows) needs to anticipate. Also not solved: a durable, resumable
  multi-step workflow *instance* (Temporal/Zeebe-shaped) — explicitly
  ruled out by the user's own "just a query" requirement, not a gap
  this design failed to close.
- `docs/comparisons/user-flow-dsl.md`'s own Recommendation section
  records this decision inline (a "## Decision" section appended, not a
  rewrite of the comparison itself); `docs/10-open-questions.md`'s
  former row 1 is deleted per that file's own no-struck-through-copy
  rule.
