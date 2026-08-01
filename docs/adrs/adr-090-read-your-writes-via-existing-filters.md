[← ADR index](../07-adrs.md)

# ADR-090: Read-your-writes stays declined as a built-in guarantee — achievable by the caller today via `EventId`/`OriginId`+`SequenceNumber` filtering, no frontier token adopted

Status: Accepted

Context: `docs/10-open-questions.md` row 21 asked whether this design
should adopt a **frontier token** (a per-origin-stream offset map
returned by every write, presented on a later read, guaranteeing that
read reflects at least the presented offsets even if served by a
lagging replica — real prior art: Cosmos DB session tokens, other
distributed databases' causal-consistency tokens) to give causal/read-
your-writes consistency across `ADR-033`'s gossip-replicated multi-site
mesh, or continue declining it as already stated. Direct design
conversation resolved this session: **no new abstraction needed** — a
caller can already get the same effect by filtering an ordinary query
against fields this design's write path already produces.

Decision:
- **Simplest case — checking one specific write**: filter/query by the
  write's own client-supplied `EventId` (`ADR-011`), already uniquely
  indexed on `StoredEvent`. "Has the event I just published been folded
  at the site I'm now reading from" is answerable today with zero schema
  change, via an ordinary `Follow`/GraphQL query.
- **General case — checking a whole batch/session's writes**: expose
  the assigned `OriginId` + `SequenceNumber` in the publish response
  envelope (`ADR-023`), so a caller can filter a subsequent query by
  "has this site applied everything from `Origin=X` through
  `SequenceNumber=Y`" — the identical semantic a frontier token would
  encode, just expressed as an ordinary, caller-managed GraphQL filter
  argument instead of an opaque token the framework manufactures and
  threads through for them.
- **This is a confirming reuse, not new machinery — `OriginId`/
  `LogicalClock` already exist on `StoredEvent` per `ADR-033`, just not
  yet propagated into `docs/data/event-log.md` (a pre-existing gap,
  fixed in this same pass) or exposed in the response envelope (fixed
  here).**
- **Read-after-write consistency remains explicitly declined as a
  built-in framework guarantee — this ADR doesn't reverse that.**
  Nothing automatically waits for a lagging replica to catch up; a
  caller who wants the behavior polls/retries their own explicit filter
  query until it returns what they expect. That's a real, accepted cost
  (client-managed polling) — the same fundamental tradeoff a frontier
  token still has (a reading node still needs to wait/retry internally
  until caught up), this design just doesn't hide that behind a token
  abstraction the framework would need to build, thread through every
  API surface, and maintain.
- **Real prior art (Cosmos DB session tokens, other causal-consistency
  tokens) was genuinely weighed, not dismissed without looking** — the
  session-token *shape* is sound in general; it just isn't the cheapest
  correct answer here, given this design already exposes the exact
  fields (`OriginId`, `SequenceNumber`) a token would otherwise need to
  opaquely encode.

Consequences:
- `docs/data/event-log.md`'s `StoredEvent` gains `OriginId`/
  `LogicalClock` (previously described in `ADR-033`'s own text but never
  propagated — a pre-existing drift-table item, now partially closed)
  and `ADR-023`'s response envelope gains `sequenceNumber`/`originId` —
  both in this same pass, per this project's data-model-ownership
  convention.
- No new interface, no new abstraction, no build-plan phase needed for a
  mechanism that doesn't exist — this ADR is scope clarification plus a
  small, concrete field exposure, not new framework surface.
- Resolves `docs/10-open-questions.md` row 21.
