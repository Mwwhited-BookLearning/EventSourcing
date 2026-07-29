[← Pattern index](README.md)

# CQRS & Materialized Views

## The pattern

**CQRS (Command Query Responsibility Segregation)** — separate the model
used to change data (commands) from the model used to read it (queries).
They don't have to share a schema, a database, or even a deployment.
**Source:** [Martin Fowler — CQRS](https://martinfowler.com/bliki/CQRS.html);
[Greg Young — CQRS Documents](https://cqrs.files.wordpress.com/2010/11/cqrs_documents.pdf) (2010).

**Materialized View** — generate a prepopulated, query-shaped view of
data that isn't stored in a form convenient to query directly. The view
is a *specialized cache*: never written to directly, entirely disposable,
rebuildable from the source of truth at any time.
**Source:** [Azure Architecture Center — Materialized View pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/materialized-view),
which states the connection to event sourcing directly: "In some systems,
like when you use the Event Sourcing pattern... materialized views are
necessary [to] obtain information from the event store."

These two patterns are practically inseparable in an event-sourced
system: the write side (the event log) is *never* a convenient shape to
query, by construction — CQRS is the architectural split that says "that's
fine, don't try to query it directly," and Materialized View is the
concrete mechanism for what you query instead.

```plantuml
@startuml CQRS_Component
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Person(writer, "Command Sender")
Person(reader, "Query Sender")

Container_Boundary(write, "Write Side") {
  Component(cmdHandler, "Command Handler", "Validates + appends")
}
ContainerDb(log, "Event Log", "Append-only, source of truth")

Container_Boundary(read, "Read Side") {
  Component(projector, "Projector", "Folds events into a query-shaped view")
}
ContainerDb(view, "Materialized View", "Disposable, rebuildable cache")

Rel(writer, cmdHandler, "Command")
Rel(cmdHandler, log, "Append")
Rel(projector, log, "Replay / tail")
Rel(projector, view, "Write (never read by a client directly)")
Rel(reader, view, "Query")
note right of view
  Never updated by a client request.
  Rebuilt by replaying the log from
  the beginning if ever lost or wrong.
end note
@enduml
```

**What it buys you:** the read side can be shaped however a given query
actually needs (denormalized, pre-joined, pre-aggregated), independent of
what the write side needs (append-only, minimal, normalized-by-event-type).
Multiple, differently-shaped read models can coexist over the same events.

**What it costs:** the read side is **eventually consistent** with the
write side, not transactionally consistent — there is always some lag
between an event landing and a materialized view reflecting it. Every
materialized view is also a second thing that must handle replay,
checkpointing, and rebuild correctly, or it silently drifts from the
truth.

## Also known as

A **Materialized View** is also called a **read model**, a **projection**
(caution — this project's own `ADR-018` deliberately avoids that word for
this exact reason: it means something specific and different, a CQRS
read model, right next to a schema-mapping sense the same word has in
`docs/design-docs/07`), or a **denormalized view**. **CQRS** is often
confused with plain **Command-Query Separation** (Bertrand Meyer's older,
narrower principle: a method should either change state or return a
value, never both) — CQRS the architectural pattern is a *different,
larger* idea (separate models, not just separate methods) that borrows
the older term's name; Fowler's own CQRS write-up flags this distinction
explicitly.

## How this application uses it

- The **write side** (`01`–`09`, `ADR-001` through `ADR-020`) is
  read-model-agnostic by design — nothing about the event log's shape
  assumes any particular query pattern will be run against it.
- **Custom projections** (`ADR-015`/`016`, `09-cqrs-read-models.md`) are
  the opt-in materialized-view mechanism: `ProjectionHost` consumes the
  public Follow API (deliberately not a private hook — `ADR-015`'s whole
  point), applies `Full`/`Partial` merge semantics centrally
  (`ADR-016`/`ADR-022`), and a rebuild is *exactly* "replay from
  `SequenceNumber` `0` again" — no separate rebuild code path, which is
  precisely the "entirely disposable, rebuildable" property the pattern
  promises.
- **The Entity Store** (`ADR-021`) generalizes this into an *always-on*
  materialized view — one canonical "current state" projection that
  exists automatically for every entity, rather than only for the
  specific read shapes someone chose to build a custom projection for.
- **Eventual consistency is stated, not hidden**: `ADR-015`'s
  consequences say plainly that this design does not attempt
  read-after-write consistency — a real, named cost of the pattern,
  called out rather than glossed over.
