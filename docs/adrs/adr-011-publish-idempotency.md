[← ADR index](../07-adrs.md)

# ADR-011: Publish idempotency via an optional client-supplied `eventId` + a stored payload hash

Status: Accepted

Context: `EventId` was always server-generated (`Guid.NewGuid()`), so the
unique index on it (`02-data-model.md`) never actually caught a real
duplicate — a publisher whose connection drops after a successful insert
but before the response arrives has no safe way to retry: retrying just
creates a second, distinct `StoredEvent` with a fresh `EventId`, a true
duplicate the store cannot detect at all.

Decision:
- The publish envelope gains an optional `eventId` field (a `Guid`,
  alongside `payload`/`parentEventIds`). Omitted: behavior is unchanged —
  the server generates a fresh `EventId`, no idempotency is possible
  (there's nothing for a retry to be checked against).
- `StoredEvent` gains a `PayloadHash` column: a SHA-256 digest (FIPS
  180-4) over a canonical serialization of `{ eventType, payload,
  parentEventIds: <sorted> }` — computed and stored on every publish,
  whether or not `eventId` was supplied.
- When `eventId` **is** supplied, `PublishEndpoint` looks it up (via the
  existing unique index) immediately after resolving the active
  `EventTypeDefinition` and the `RequiredPublishClaim` check
  (`ADR-008`) — before schema/parent-link validation, as a short-circuit:
  - **Not found**: proceed exactly as the unsupplied case, except the
    caller's `eventId` is used for the new row instead of a generated one.
  - **Found, `PayloadHash` matches** the incoming request's: this is an
    **idempotent replay** — return the identical response as the original
    successful publish (`201`, same body). No new row, no re-validation;
    the store performs no write at all.
  - **Found, `PayloadHash` differs**: `409 Conflict` — the same `eventId`
    was reused for genuinely different content. This is a caller bug
    (idempotency-key reuse), not silently accepted and not treated as a
    fresh publish.

Consequences:
- This is opt-in: a publisher that never supplies `eventId` gets no
  idempotency guarantee, same as before this ADR — an accepted trade
  rather than forcing every publisher to manage an idempotency key.
- The hash **must** include `eventType`, not just `payload` and
  `parentEventIds` — otherwise two genuinely different event types
  publishing byte-identical payload/parent content could collide
  undetected as "the same request retried," which they are not.
- Two concurrent retries with the same never-yet-seen `eventId` can both
  pass the "not found" check before either commits, and race at the
  database's unique-constraint level on insert. The loser's insert fails;
  it must catch that specific constraint violation and re-run the lookup
  (which will now find the winner's row) rather than surfacing a raw DB
  error — functionally the same "found, compare hash" path, just entered
  via a failed insert instead of a preceding `SELECT`.
- An idempotent replay skips schema and parent-link validation entirely
  (it already passed the first time) — this means a schema *version*
  change between the original publish and a much-later retry with the
  same `eventId` has no effect on the replay; it returns the original,
  historically-valid result, consistent with `StoredEvent.SchemaVersion`
  recording whichever version validated a given event at the time
  (`05-schema-registry-and-spec-generation.md`).
- `PayloadHash` has no index of its own — the unique index on `EventId` is
  what makes the lookup fast; the hash is only consulted after that lookup
  finds a match, purely as a content-equality check.
- `PayloadHash` answers content-equality only — it says nothing about
  tamper-evidence across the store's history. `ADR-019` reuses the same
  SHA-256 primitive to build a hash *chain* (`ChainHash`) on top of every
  `StoredEvent`, a genuinely different guarantee layered on the same
  computation this ADR introduces.
