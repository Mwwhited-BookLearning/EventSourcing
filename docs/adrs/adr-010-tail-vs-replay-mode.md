[← ADR index](../07-adrs.md)

# ADR-010: Explicit tail-vs-replay mode on Follow, via a `mode` parameter

Status: Accepted

Context: `/follow/{event-type}` previously had no way to ask for
anything other than new events from the moment of connecting — a caller
who wanted the matching history that already exists in the store first had
no path to it short of a separate, unspecified mechanism.
`04-odata-filter-pushdown.md` had gestured at "tailing from connection
time or a resume token" without ever specifying either. (Written when
Follow was still `GET`; `ADR-012` later moves it to the HTTP `QUERY`
method — an unrelated, purely transport-level change made after this one,
which is why "a `mode` parameter" below deliberately avoids the phrase
"query parameter," to not collide with that later method name.)

Decision:
- `/follow/{event-type}` gains a `mode` parameter:
  `mode=tail` (**default** — unchanged from the existing behavior, no
  history) or `mode=replay`.
- `mode=replay` accepts an optional `fromSequenceNumber` (non-negative
  integer, default `0`): replay every matching event with
  `SequenceNumber > fromSequenceNumber`, then — with no gap and no
  duplicate — keep streaming new matching events exactly as `mode=tail`
  already does. This is one continuous poll loop
  (`WHERE SequenceNumber > lastSeen AND predicate`,
  `04-odata-filter-pushdown.md`), not two separate code paths: the only
  difference between the modes is the *initial* value of `lastSeen` —
  "current max `SequenceNumber` at connect time" for `tail`, `fromSequenceNumber`
  (or `0`) for `replay`.
- `fromSequenceNumber` is rejected (`400`) if supplied together with
  `mode=tail` (or the default) — silently ignoring it would let a caller
  believe they got a replay they didn't get.
- Applies uniformly regardless of `$filter`: replay only returns matching
  (filtered) historical events, using the same predicate as live tailing —
  no special-casing needed, per the "one continuous poll loop" point
  above. Applies uniformly regardless of `RequiredReadClaim` (`ADR-008`)
  and masking (`ADR-009`) too — both are checked once at connect time,
  independent of which mode was requested.

Consequences:
- `fromSequenceNumber` is a **raw sequence number the consumer must track
  themselves** (from the `sequenceNumber` field already present on every
  streamed event's envelope headers) — this is deliberately not a
  server-managed consumer-group checkpoint the way Kafka's committed
  offsets are. A consumer that wants to resume after a disconnect persists
  the last `sequenceNumber` it successfully processed and reconnects with
  `mode=replay&fromSequenceNumber=<that value>`; the store keeps no
  per-consumer state at all.
- Connecting with `mode=replay&fromSequenceNumber=0` against an event type
  with a large amount of history bursts that entire matching history at
  the caller as fast as the connection can carry it — there is no
  batching, pacing, or backpressure control on the replay burst. Consumers
  must be able to absorb that burst; this is an accepted v1 limitation, not
  solved here.
- This resolves `04-odata-filter-pushdown.md`'s previously-vague "tailing
  from connection time or a resume token" mention — that line is removed
  from "out of scope" now that it's specified here instead.
- A `mode=replay` burst against a long-lived event type can span every
  schema version that type has ever had — this ADR says nothing about
  reconciling those different shapes into one; `ADR-018` (event upcasting)
  is what actually resolves that, layered on top of the cursor mechanics
  decided here.
