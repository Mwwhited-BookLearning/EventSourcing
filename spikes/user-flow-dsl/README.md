# User-flow DSL spikes

All spikes in this folder implement the exact same scenario from
[`docs/comparisons/user-flow-dsl.md`](../../docs/comparisons/user-flow-dsl.md)
(Vitals' Adverse Event review) and are run for real, three ways: a
serious adverse event accepted, one rejected, and an ordinary
non-serious publish. None is wired into `EventStore.slnx` — this is
throwaway research code, not a production dependency (`docs/10-open-
questions.md` row 1 is still not decided).

Run any of them with `dotnet run` from its own directory.

## At a glance

| Spike | Framework(s) | What it does | Configured by | Text-file format |
|---|---|---|---|---|
| [`PlantUmlNativeSpike/`](PlantUmlNativeSpike/) (Option G1) | [PlantUML](https://plantuml.com/activity-diagram-beta) Activity Diagram syntax — no parsing framework, hand-rolled | Hand-rolled recursive-descent parser + interpreter for a constrained PlantUML Activity Diagram subset, executed directly against a C# delegate registry | Text file | PlantUML Activity Diagram (`.puml`) |
| [`ElsaSpike/`](ElsaSpike/) (Option B) | [Elsa Workflows](https://elsaworkflows.io) 3.7.1 | A `Sequence`/`If` workflow with a custom blocking/bookmark activity (`WaitForAuthorityDecisionActivity`), resumed via a real bookmark round trip | Code (C# fluent workflow-builder API) | — none |
| [`AntlrCustomDslSpike/`](AntlrCustomDslSpike/) (Option G2) | [ANTLR4](https://www.antlr.org) (`Antlr4BuildTasks`/`Antlr4.Runtime.Standard`) | A wholly custom textual DSL — a real `.g4` grammar compiled into a Visitor-pattern parser, walked into a small AST/interpreter | Text file | Custom `UserFlowDsl` grammar (`.flow`, defined by `Grammar/UserFlowDsl.g4`) |
| [`NRulesDmnSpike/`](NRulesDmnSpike/) (Option E) | [NRules](https://nrules.net) (RETE engine) + [`net.adamec.lib.common.dmn.engine`](https://github.com/adamecr/Common.DMN.Engine) (DMN) | NRules forward-chaining rules drive the flow's sequencing (no AST); the one multi-factor classification decision is delegated to a real DMN table | Both — rules by code (NRules Fluent DSL), classification by text file | DMN 1.3, the real OMG standard (`.dmn`) |
| [`TemporalSpike/`](TemporalSpike/) (Option C) | [Temporal](https://temporal.io) (.NET SDK) | Real `[Workflow]`/`[Activity]`/`[WorkflowSignal]` classes run against a locally-downloaded Temporal Server | Code (C# classes with Temporal SDK attributes) | — none |
| [`ZeebeSpike/`](ZeebeSpike/) (Option D) | [Camunda 8 / Zeebe](https://camunda.io) (`zb-client`) | A real BPMN 2.0 process deployed to and executed by a real Zeebe broker, with job workers polling via the SDK's own lower-level primitives | Text file | BPMN 2.0 XML (`.bpmn`) |
| [`PlantBpmnSpike/`](PlantBpmnSpike/) (Option H) | [PlantBPMN](https://codeberg.org/Some1/PlantBPMN) (PlantUML → BPMN) + XSLT (.NET `XslCompiledTransform`) | PlantUML compiled to real BPMN XML via PlantBPMN; a real XSLT stylesheet renders a BPMN file back into PlantUML text (the reverse direction) | Text file, in both directions | PlantUML (`.puml`, input) → BPMN 2.0 XML (`.bpmn`, compiled) → XSLT 1.0 stylesheet (`.xslt`, drives the reverse render) |

**[`SchemaValidation/`](SchemaValidation/)** isn't a spike — it's a real
validator (`dotnet run`, CI-usable exit code) checking the DMN/BPMN/XSLT
files above against real, checked-in official OMG schemas. Its own
[`README.md`](SchemaValidation/README.md) also covers, and is honest
about, the two formats with no formal schema at all (PlantUML, the
custom `UserFlowDsl` grammar).

- **`PlantUmlNativeSpike/`** — Option G1 (hand-authored PlantUML Activity
  Diagram, parsed and executed directly). See below.
- **`ElsaSpike/`** — Option B (Elsa Workflows). See below.
- **`AntlrCustomDslSpike/`** — Option G2 (wholly custom textual DSL via a
  real ANTLR4 `.g4` grammar). See its own
  [`README.md`](AntlrCustomDslSpike/README.md) for the grammar/instance
  file-schema relationship and findings.
- **`NRulesDmnSpike/`** — Option E (NRules rule engine + a real DMN 1.3
  decision table). See its own
  [`README.md`](NRulesDmnSpike/README.md) — the flow has no AST at all,
  only forward-chaining rules matched against accumulating facts, and
  the "wait for human input" pause point falls out of that for free.
- **`TemporalSpike/`** — Option C (Temporal durable execution). See its
  own [`README.md`](TemporalSpike/README.md) — runs against a real,
  locally-downloaded Temporal Server (no manual Docker setup), and
  worked correctly on the first real run with no API-mismatch friction
  at all, the smoothest integration of any spike in this folder.
- **`ZeebeSpike/`** — Option D (Camunda 8 / Zeebe, real BPMN 2.0 engine).
  See its own [`README.md`](ZeebeSpike/README.md) — needs a real broker
  container (docker run command included) and, by a wide margin, the
  most operational friction of any spike here: three undocumented env
  vars just to reach an unauthenticated local broker, plus a high-level
  client API that silently never worked, worked around with a
  hand-rolled polling loop using the same SDK's lower-level primitives.
- **`PlantBpmnSpike/`** — Option H (PlantUML compiled to real BPMN via
  PlantBPMN, plus a real XSLT reverse-visualizer back to PlantUML). See
  its own [`README.md`](PlantBpmnSpike/README.md) — the forward compile
  step genuinely works for a flat branch, but a real, reproducible
  PlantBPMN defect (a dead-end join gateway) shows up the moment the
  scenario's actual *nested* if/else shape is tried; never reaches
  Zeebe, since PlantBPMN only targets Flowable.

## PlantUML-native (`PlantUmlNativeSpike/`) — Option G1

Parses the **exact, unmodified** `.puml` file already committed at
`docs/diagrams/comparisons/user-flow-dsl/01-option-f-hand-authored-
plantuml-activity-diagrams-.puml` and executes it directly against a
small, explicit registry of C# delegates. ~150 lines total (parser +
interpreter + program), zero NuGet dependencies.

**Worked on the second try.** The one real bug found: a C# string
literal's `"\n"` is an actual newline character, but the `.puml` file's
own `\n` is PlantUML's literal two-character line-break escape — a
genuine "two different escaping conventions collide" gotcha, not a
typo. Fixed with `"\\n"`. This is a real, ongoing cost of this approach
worth naming honestly: every action/condition label must match the
diagram's own text *exactly*, including any embedded escape sequences,
which is a real (if narrow) maintenance burden as diagrams evolve.

## Elsa Workflows (`ElsaSpike/`) — Option B

Real `Elsa` NuGet package (**version 3.7.1** — see the version note
below), a custom `WaitForAuthorityDecisionActivity` (blocking/bookmark),
`Sequence`/`If` composing the same branching shape.

**Took substantially more real, verified friction to get working than
the comparison doc's own research alone predicted** — all found only by
actually running it, not by reading docs:

1. **A version discrepancy worth flagging.** The comparison doc's own
   Option B section cites Elsa **v4** as the version with real BPMN 2.0
   import/export. The actual, current, installable package via
   `dotnet add package Elsa` is **3.7.1** — v4 may not yet be on NuGet,
   or is distributed differently; not resolved further here, but the
   comparison doc's own BPMN claim should be read as "a claimed v4
   feature not yet verified against an actually-installable package,"
   not confirmed.
2. **The official docs page I'd quoted directly in the comparison
   doc turned out to be stale against the real, installed 3.7.1 API.**
   `docs.elsaworkflows.io`'s own "Blocking Activities & Triggers" page
   shows `CreateBookmarkArgs.Payload` and `ActivityExecutionContext
   .GetWorkflowInput<T>()`/`.SetResult()` — none of these exist on the
   real, installed type (confirmed via reflection against the actual
   assembly, not assumed). The real members are `CreateBookmarkArgs
   .Stimulus`, and resume input arrives via the `WorkflowInput`
   dictionary property with no generic-typed accessor at all.
3. **`Input<T>` lives in `Elsa.Workflows.Models`, not `Elsa.Workflows`**
   — despite `Activity`/`ActivityExecutionContext` living directly in
   `Elsa.Workflows` — an inconsistent namespace layout that cost real
   time via misleading "type not found" errors before checking the
   assembly directly.
4. **A silent-wrong-behavior trap, not just an error message**: calling
   the general `RunAsync(workflow, workflowState, options)` resume
   overload *without* setting `options.BookmarkId` doesn't fail — it
   silently **re-executes the entire workflow from the beginning**
   instead of continuing from the paused activity. Confirmed directly:
   the first version of this spike produced every "before the pause"
   `WriteLine` twice, with no error of any kind, before this was caught
   and fixed by explicitly setting `BookmarkId` from `WorkflowState
   .Bookmarks`.
5. **Composing a resumed decision into a later `If`'s `Condition` via
   the blocking activity's own declared `Output<bool>` fails after a
   real bookmark/resume round trip**, with `Elsa.Workflows.Exceptions
   .InputEvaluationException: Could not find a descriptor for
   expression type "Output"` — an `Output<T>` reference between two
   activity instances doesn't survive the state serialization a real
   resume goes through. The fix: a workflow-level `Variable<bool>`
   (Elsa's own designed-for-persistence storage) instead — this then
   worked correctly. Nowhere found documented as a distinction that
   matters; discovered only by the concrete failure.

None of this is a claim that Elsa doesn't work — it does, correctly,
once all five of the above are known. The claim is narrower and more
useful: **the real integration cost of Option B was materially higher
than either the comparison doc's own research or Elsa's own official
docs suggested**, entirely because of gaps between documentation and
the actually-installed package version, not because the underlying
bookmark/resume mechanism is unsound.
