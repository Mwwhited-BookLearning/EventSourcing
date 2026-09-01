# PlantBPMN + XSLT spike — Option H

Proves **both halves** of Option H from
[`docs/comparisons/user-flow-dsl.md`](../../../docs/comparisons/user-flow-dsl.md):

1. The **forward** direction: author clean PlantUML text, compile it to
   real BPMN 2.0 XML via the real
   [PlantBPMN](https://codeberg.org/Some1/PlantBPMN) Go tool — closing a
   gap the comparison doc's own Option H section explicitly flagged as
   "not independently verified this pass" before this spike.
2. The **reverse** direction, and the literal original ask this whole
   comparison traces back to ("a custom visualizer... like an XSLT over
   BPMN files to PlantUML diagrams"): a real XSLT 1.0 stylesheet, applied
   via .NET's built-in `System.Xml.Xsl.XslCompiledTransform`, that
   renders a real BPMN file back into readable PlantUML text.

## Running it

Step 1 needs Go, which isn't installed locally — run PlantBPMN via
Docker instead (already done for both `.puml` files in this repo;
`Generated/*.bpmn` are checked in as real, unedited evidence of these
exact runs):

```bash
docker run --rm \
  -v "$(pwd)/Puml:/work/Puml" -v "$(pwd)/Generated:/work/Generated" \
  -v goplantbpmn-cache:/root/go -w /work golang:1.25 \
  go run codeberg.org/Some1/PlantBPMN@latest \
  -pumlFile=Puml/AdverseEventReviewFlat.puml -bpmnFile=Generated/AdverseEventReviewFlat.bpmn
```

Step 2 is `dotnet run` from this directory — no Docker needed, just the
real, checked-in `.bpmn` files and the XSLT.

## Two PlantUML sources, one real finding between them

| File | Shape | Result |
|---|---|---|
| `Puml/AdverseEventReviewFlat.puml` | Single-level `if/else` (mirrors the outer `SeriousAdverseEvent?` branch only) | Compiles to a correctly-connected BPMN graph; round-trips through the XSLT perfectly |
| `Puml/AdverseEventReview.puml` | The *same* scenario as every other spike in this folder, with a **nested** `if (accepted) ... else ...` inside the outer `yes` branch | Compiles, but the inner if/else's join gateway comes out with **zero outgoing `sequenceFlow` elements** — a real dead end |

**This is a genuine, previously-unverified defect in PlantBPMN itself,
found only by generating the real output and tracing its actual graph**
— confirmed directly:

```bash
grep -n 'sourceRef="PlantBPMN-13bfa39b' Generated/AdverseEventReview.bpmn
# (no output at all -- nothing in the file ever flows OUT of that gateway)
```

Tracing the surrounding structure shows why: the task right before the
nested `if` (`PI publishes authorityDecision`) ends up with **two**
outgoing `sequenceFlow` elements in the generated file — one correctly
into the nested gateway, and a second one that skips the nested if/else
entirely and jumps straight to the *outer* `endif`. The inner if/else's
own two branches (`Fold now catch-up` / `Entity Store left untouched`)
both converge on a join gateway that itself goes nowhere. Whatever
internal step PlantBPMN uses to wire a nested conditional's continuation
appears to attach the outer continuation to the wrong node when a second
`if` is nested one level deep — a real, reproducible generator bug, not
a one-off fluke (rebuilding from the same `.puml` twice produces the
same broken shape both times).

**Checked whether an older PlantBPMN release avoids this, and whether
it's already a known, reported issue** — neither escape hatch exists.
Codeberg's own issue tracker for this project shows **zero** open or
closed issues at all, so this is genuinely unreported. Of the five
release tags (`1.0.0`, `v1.0.1`–`v1.0.4`), `v1.0.4` (what `@latest`
resolves to, and what this spike used throughout) is the **only one
actually invocable** via the documented `go run
codeberg.org/Some1/PlantBPMN@<version>` form: `1.0.0` isn't a resolvable
Go module version at all (`invalid version: unknown revision v1.0.0`),
and `v1.0.1`–`v1.0.3` all panic immediately with `open ./templates: no
such file or directory` — those releases apparently look up their BPMN
XML templates via a path relative to the process's working directory
rather than an embedded (`//go:embed`) resource, which only resolves
inside a full local clone of the repo, not when Go fetches and runs the
module from its module cache. So there is no older, still-runnable
version of this tool to regression-test the nested-if defect against
this way — `v1.0.4` is simultaneously the newest release and the only
one this invocation method can reach at all.

## The XSLT (`Xslt/BpmnToPlantUml.xslt`)

Walks the BPMN graph via `sourceRef`/`targetRef` keys, not document
order — a real, confirmed necessity: PlantBPMN's own generated XML
interleaves an if/else's two branches around their shared join gateway
element rather than emitting the join after both, so a naive
`xsl:for-each` over child elements in file order does **not** reconstruct
correct control flow.

XSLT 1.0 has no native way to return two values from a recursive
`xsl:call-template` (the rendered text, and which join gateway a branch
stopped at), so both are packed into one string with a Private Use Area
separator character (`&#xE000;`) and split back apart with
`substring-before`/`substring-after` at the call site — a real,
if slightly unusual, pure-XSLT-1.0 technique, not a library or extension
function.

## Findings

**The flat, single-level source round-trips perfectly** — `dotnet run`'s
first block reproduces the original PlantUML almost exactly (minus the
`:start [event="..."];` line, which PlantBPMN maps onto a real BPMN
message start event and this XSLT correctly treats as a non-rendered
entry point rather than an action). This is genuine, working proof of
both the forward compile step and the reverse XSLT visualizer.

**The nested source exposes the real PlantBPMN defect above** — its
block prints an empty `yes` branch instead of four actions and the
inner if/else, exactly matching where the graph actually goes dead.
Debugging this (via a throwaway diagnostic variant of the XSLT printing
intermediate flow-selection state) surfaced a second, smaller, genuinely
interesting XSLT 1.0 characteristic worth naming on its own: when
`substring-before`/`substring-after`'s search string isn't found *at
all*, XPath 1.0 returns an empty string for the **whole** result, not a
best-effort truncation — so a defect several recursion levels deep
doesn't just corrupt its own immediate output, it silently erases every
enclosing branch's text on the way back up the call stack. A defensive
fallback branch in the XSLT (emitting `UNKNOWN kind=[...] id=[...]`) does
fire when this happens, confirmed via manual debugging with an
intermediate `xsl:variable`, but even that text never survives to the
final printed output for exactly this reason — documented in the XSLT's
own comment at that branch rather than left as a silent surprise.

**Was Option D's Zeebe broker reused here?** No, deliberately — real
research before building confirmed PlantBPMN's own `-target` flag
currently only supports Flowable-flavored BPMN output (`xmlns:flowable`,
no `zeebe:` extension elements anywhere in the generated file), not
Zeebe. Deploying this tool's real output to the Zeebe broker
`ZeebeSpike/` already stands up would need a genuine dialect-bridging
transform (Flowable's `flowable:class`/`flowable:expression` extensions
into Zeebe's `zeebe:taskDefinition`/`zeebe:subscription`) as real,
separate scope, not built this pass — noted here as an honest boundary
rather than silently assumed away.
