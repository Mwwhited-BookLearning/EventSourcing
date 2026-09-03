[← Pattern index](README.md)

# Watermarks / Event-Time vs. Processing-Time Ordering

## The pattern

Distinguish two different notions of "when" for a piece of streaming
data: **event time** is when the event actually occurred, out in the
real world; **processing time** is when the system doing the processing
observes it. The two are never guaranteed to match — network delay,
buffering, offline capture, and retries all mean data can arrive well
after (or, more rarely, before) its own event time would suggest. Fold
or aggregate strictly by processing/arrival order and a late-arriving
event silently reverts or corrupts already-computed results based on
newer data, because arrival order was never actually a promise about
logical order. A **watermark** is the mechanism systems use to reason
about this honestly: a notion of input completeness with respect to
event time — "a watermark with value X asserts that all input data with
event times less than X have been observed" — treated explicitly as a
heuristic guess about a distributed, unreliable world, not a guarantee,
with defined handling for the (expected, not exceptional) case where a
later event still arrives after the watermark already passed its event
time.

**Source:** Tyler Akidau's **"Streaming 101: The World Beyond Batch"**
and its follow-up **"Streaming 102"**
([O'Reilly Radar](https://www.oreilly.com/radar/the-world-beyond-batch-streaming-101/)),
later expanded into the book *Streaming Systems* (Akidau, Chernyak, Lax)
— the canonical, most-cited framing of event-time-vs-processing-time and
the watermark concept; the same vocabulary and mechanism is documented
directly in
[Apache Flink's own "Timely Stream Processing" docs](https://nightlies.apache.org/flink/flink-docs-stable/docs/concepts/time/)
and underpins Google Cloud Dataflow/Apache Beam's windowing model.

```plantuml
@startuml Watermarks_EventTime
title Event time vs. processing time, with a late arrival

robust "Processing time (arrival order)" as PT
robust "Event time (when it actually happened)" as ET

@PT
0 is Idle
0 is A
2 is B
2 is C
6 is "D (late!)"

@ET
0 is Idle
0 is "A (t=0)"
2 is "B (t=2)"
2 is "C (t=5)"
6 is "D (t=1) -- arrives at PT=6, but its event time (1) is\nolder than B/C's, which already folded/won at their properties"

note bottom
  A watermark advancing past event-time=1 before D arrives is the
  "guess turned out wrong" case this pattern names explicitly --
  D is flagged as a late arrival rather than silently overwriting
  whatever already folded on top of it.
end note
@enduml
```

## When you'd reach for it

Any fold, aggregation, or materialized view built by consuming a stream
of events where arrival order and logical occurrence order can diverge
— offline-capable clients that buffer and sync later, retried publishes
after a network gap, multi-source ingestion where different producers
lag by different amounts. If "whatever arrives last wins" would ever
silently undo a correct, newer value with a stale one just because it
happened to take longer to arrive, this is the pattern that names the
problem and gives it principled handling instead of an accidental bug.

## Cost

Tracking a high-water mark (or per-key watermark) is extra state to
maintain and reason about, and the "best as possible" default — flag a
late arrival and don't apply it, rather than either blindly applying it
or dropping it — is explicitly an approximation, not the mathematically
exact answer; getting the exact answer back requires a full replay in
true logical order, which costs real recomputation time and generally
can't be done incrementally the way the fast, approximate path can. Per
-key (or, in this codebase's case, per-property) granularity fixes a
real correctness gap that per-entity/per-stream granularity misses, but
costs more state to track than the coarser default.

## How this application uses it

`ADR-029` names this pattern explicitly and applies it to the Entity
Store fold: `StoredEvent.OccurredAt` is defined as the event's
client-declared **event time**, distinct from arrival/`SequenceNumber`
order (**processing time**). The Entity Store row carries a high-water
mark — originally per-entity, and since 2026-08-12 tracked per-property
via `EntityStoreRow.PropertyLogicalTimes` — compared against each
incoming event's `OccurredAt`. An event whose `OccurredAt` is at or
before the high-water mark for a touched property is a **late
arrival**: flagged (`LateArrivalFlag`) and excluded from the merge for
that property specifically, rather than either applied (which would
silently revert already-folded newer data) or dropped (it stays fully
present in the immutable log and entity change history). This is
implemented in
[`src/EventStore.Router/RouterWorker.cs`](../../src/EventStore.Router/RouterWorker.cs)'s
`FoldAsync`, which also keeps `LastAppliedSequenceNumber` (the replay
checkpoint, which always advances) and `Version` (the data-change
counter, which does not advance for a rejected late arrival) explicitly
distinct. The exact, on-demand alternative `ADR-029` names — replaying
all of an entity's events strictly in `OccurredAt` order for the
mathematically precise answer — is the same "cheap-and-flagged now,
exact-and-expensive on demand" two-tier shape this design already uses
elsewhere (masking strategies, `ADR-009`; conflict-resolution
sophistication, `ADR-024`).
