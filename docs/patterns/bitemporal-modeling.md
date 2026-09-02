[← Pattern index](README.md)

# Bitemporal Modeling (Valid Time vs. Transaction Time)

## The pattern

Track two independent time axes for the same fact, rather than one.
**Valid time** is when something was true (or became true) in reality —
independent of any computer system ever recording it. **Transaction
time** is when the database itself learned about it — when the row was
actually inserted, and (in a fully append-only system) never overwritten
after that. A table associated with only valid time is a *valid-time
state table*; one associated with only transaction time is a
*transaction-time state table*; one that tracks both simultaneously is a
**bitemporal table**. Keeping both axes explicit and independently
queryable lets two genuinely different questions both be answered
honestly: "what do we know *now* to be true, as of some point in
reality" (a valid-time query) and "what did the system *show* at some
point in the past, including whatever was wrong or incomplete at the
time" (a transaction-time query). Collapsing the two into a single
timestamp — as most systems that only ever add a `CreatedAt` column do —
makes the second question unanswerable once a late correction arrives:
there's no way to tell whether a value changed because reality changed
or because the system's understanding of reality changed.

**Source:** Richard T. Snodgrass's academic and applied work on temporal
databases (notably *Developing Time-Oriented Database Applications in
SQL*) is the origin of the valid-time/transaction-time terminology and
the term *bitemporal* itself; this vocabulary was carried into the
[SQL:2011](https://www.iso.org/standard/53683.html) standard's
system-versioned tables (transaction time) and application-time period
tables (valid time), and into concrete shipped implementations such as
[SQL Server's `FOR SYSTEM_TIME AS OF`](https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal-tables?view=sql-server-ver17).

```plantuml
@startuml Bitemporal_Axes
title Two independent time axes over one fact

concise "Valid time (reality)" as VT
concise "Transaction time (system knowledge)" as TT

@VT
0 is "unknown"
5 is "Address = A"
9 is "Address = B"

@TT
0 is "unknown"
6 is "recorded: Address = A"
10 is "recorded: Address = B (late — should have arrived at t=9)"
12 is "correction: back-record Address = B, valid from t=9"

@0
VT -> TT : fact starts existing
@6
TT -> TT : system learns "A" (as of VT=5)
@10
TT -> TT : system learns "B" (as of VT=9) -- arrives late in transaction time
note top: A query "as system showed at TT=8" still correctly returns A --\nnot smoothed away, not silently corrected in place.
@enduml
```

## Also known as

Sometimes shortened to just **"temporal tables"** when a system only
implements one axis (commonly transaction time only, e.g. SQL Server's
`SYSTEM_VERSIONING`) — that is a *unitemporal* table, not a bitemporal
one; the two are related but distinct, and a table advertised as
"temporal" is worth checking against which axis (or both) it actually
tracks before assuming it answers a bitemporal question.

## When you'd reach for it

Whenever "what did we show/believe at time T" is a real, distinct
question from "what is true as of time T" — audit and compliance
reporting, litigation/e-discovery review, regulatory reconstruction of a
historical decision, or any domain where a value can be corrected
*after the fact* (a late lab result, a backdated correction, a
retroactive amendment) and both the original (possibly wrong) picture
and the corrected one need to stay independently queryable, rather than
one silently overwriting the other.

## Cost

Two axes are genuinely more to reason about than one: every query has to
be explicit about which axis (or both) it's asking over, and a naive
implementation is tempted to conflate "when did this happen" with "when
did we find out" the moment nobody's paying close attention — the two
only ever diverge when something arrives late or gets corrected, so the
bug hides until exactly the moment it matters. A full bitemporal query
engine (as-of-both-axes, at arbitrary points) is also a real
implementation cost beyond a single, simpler temporal axis — most
systems only need it for specific, narrow read paths, not as a general
capability everywhere.

## How this application uses it

`ADR-068` recognizes that this design already captures both axes without
having named them: **valid time is `StoredEvent.OccurredAt`** (`ADR-029`
— the event's client-declared logical occurrence time), and
**transaction time is `SequenceNumber`/arrival order** (when the system
durably learned about it). The authoritative Entity Store fold
(`ADR-021`, `ADR-029`) is a **valid-time-corrected** view — it folds in
`OccurredAt` order specifically so a late arrival can't silently
overwrite newer data, which answers "what do we now know is true," and
is deliberately wrong for "what did the system show at the time."
`ADR-068` adds the second, previously-missing axis as a new, parallel
read mode: reconstruct an entity by folding only events with
`SequenceNumber <= T`, applied in arrival order with **no** logical-time
correction — the literal opposite of `ADR-029`'s fold rule, so a
litigation reviewer sees a late-arriving value being recovered *in
place, as it happened*, never smoothed away. This is implemented in
[`src/EventStore.LineageExport/BitemporalPlaybackService.cs`](../../src/EventStore.LineageExport/BitemporalPlaybackService.cs)'s
`ReconstructAsync(entityId, asOfSequenceNumber, ...)`, with VCR-style
play/rewind/fast-forward as a stepping interface over consecutive
`SequenceNumber` positions on top of it. Masking/erasure enforcement
(`ADR-009`/`ADR-057`) and read-access audit logging (`ADR-045`) apply
identically to this new read mode — `ADR-068` is a new *ordering* of
history, not a new *authorization* surface.
