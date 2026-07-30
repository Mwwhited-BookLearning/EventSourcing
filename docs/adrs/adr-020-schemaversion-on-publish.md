[← ADR index](../07-adrs.md)

# ADR-020: Explicit `schemaVersion` on publish, with publish-time upcast validation and a reserved dead-letter event type

Status: Accepted

Context: Publish previously validated every payload against whichever
schema version was currently active — the publisher never stated which
version they were written against, the store simply used "whatever's
active right now." That's fine until a schema evolves faster than every
publisher upgrades: a publisher still emitting a payload shaped for an
older version has no way to say so, and gets rejected against a schema it
was never written against. Separately, `ADR-018`'s registration-time
`upcastFromPrevious` validation (parses, aliases resolve) still can't
confirm the *output* of a real upcast actually satisfies the destination
schema — because there's no representative data to run it against at
registration time.

Decision:
- The publish envelope gains a **required** `schemaVersion` field,
  alongside `payload`/`parentEventIds?`/`eventId?` — the publisher states
  which version of `{event-type}`'s schema their payload is shaped for.
  `SchemaValidationService` validates against *that* version specifically
  — ~~rejected `400`, `unknown-schema-version`, if it doesn't exist~~
  **superseded by `ADR-023`, found and fixed by a design review this
  session**: an unknown `schemaVersion` now persists with `202 Accepted`
  and `SchemaStatus: invalid`, exactly like any other schema-shape
  problem, never rejected — `ADR-013`'s Problem Details table already
  documents this correction, this ADR's own text just never got the
  matching update until now. Not automatically "whichever is active."
- If `schemaVersion` names a version **behind** the event type's current
  active version, `PublishEndpoint` runs the declared payload through
  `UpcastChain` (`ADR-018`) — the same chain a Follow/`ProjectionHost`
  consumer would apply on read — all the way to the current active
  version, **as part of the publish request itself**, using the caller's
  real, just-validated payload as the test case. This is the mechanism
  that answers the compatibility-enforcement question: instead of
  checking an abstract "is this mapping compatible" question against
  synthetic data at registration time, every real publish against a
  lagging version is itself a live compatibility check against real data,
  the first time it actually matters.
- **On success** (every hop parses, evaluates, and the final result
  validates against the current version's schema): behavior is otherwise
  unchanged — the event is stored exactly as declared, at `schemaVersion`.
  `Payload` is never transformed before storage; only a Follow/
  `ProjectionHost` reader sees the upcasted shape, exactly as `ADR-018`
  already designs. The publish-time run is a validation pass, not a
  storage-shape change.
- **On failure** (a hop between `schemaVersion` and the active version
  fails to parse, fails to evaluate, or its output doesn't validate
  against that hop's target schema): the store does **not** reject the
  publish outright, and does **not** silently store an unreconcilable
  event as if nothing were wrong. It publishes a **reserved,
  system-defined event type**, `EventUpcastFailed`, in the original
  event's place. Its payload carries: the original `eventType`, the
  original `schemaVersion`, the original (verbatim, unmodified) submitted
  payload, and which `(EventType, FromVersion)` hop failed and why. ~~The
  HTTP response is still `201 Created`~~ — **corrected to `202 Accepted`**,
  matching `ADR-023`'s always-`202` publish response adopted after this
  ADR was originally written (found and fixed by a design review this
  session) — something durable *was*
  recorded — but its body names `EventUpcastFailed` as the stored type,
  not the caller's originally-intended one.
- `EventUpcastFailed` is the first **system-owned event type** in this
  design — not registered through `PUT /registry/{event-type}` by an
  operator, but reserved at the platform level. It's fully queryable like
  any other type: `QUERY /follow/EventUpcastFailed` lets an operator (or a
  monitoring projection) watch upgrade failures as they happen, rather
  than needing to poll logs.
- **Deliberately no proactive/synthetic-data check for a hop nobody has
  exercised yet, and none is wanted.** A hop between two versions that no
  lagging publisher has ever actually hit has no observable behavior at
  all until it's hit — there's nothing to validate ahead of time that
  would change anything. If it's genuinely broken, the very first real
  publish against it discovers that immediately via the
  `EventUpcastFailed` path above; if it's never exercised, whether it
  "would have worked" has no consequence for anything this system does.
  Spending effort on synthetic representative-data validation for that
  case would validate a guarantee nothing in this design actually needs.

Consequences:
- Every real publish against a lagging version doubles as a live test of
  that version's upcast path, discovered lazily and only when it matters
  — this is the intended failure mode, not a gap to close further.
- `schemaVersion` being **required**, not optional-defaulting-to-active,
  is a breaking change to every existing publish example across this
  design package's feature docs and Gherkin scenarios, none of which
  state one today — flagged as a real cross-doc follow-up, not
  exhaustively rewritten here.
- `EventUpcastFailed` needs its own fixed schema (source type, source
  version, verbatim original payload, failed-hop identifier, failure
  reason) baked into the platform rather than the registry — the first
  event type this design has that an operator didn't register. Its
  `ChangeKind`/claims answer (most likely `Full`, no claims beyond the
  base scopes, so it's visible to the same audience as the failing
  attempt) isn't designed further here.
- `EventAppender`'s idempotency check (`ADR-011`) still applies to
  whichever event actually gets stored — a retried publish with the same
  `eventId` and an upcast failure replays the *original*
  `EventUpcastFailed` response, not a fresh attempt.
- Does not change `ADR-018`'s read-time `UpcastChain` at all — it remains
  necessary and unchanged for every event stored before this ADR existed,
  or stored under a hop whose `compute()` clause only broke *after* that
  event was already written. Publish-time validation and read-time
  upcasting are complementary, not redundant with each other.
