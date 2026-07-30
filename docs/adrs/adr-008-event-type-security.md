[← ADR index](../07-adrs.md)

# ADR-008: Event-type security via per-event-type required claims

Status: Accepted

Context: The four scopes from `ADR-006` (`events:publish`, `events:follow`,
`events:lineage:read`, `registry:admin`) answer "can this caller call this
*operation* at all" — they're static and identical for every event type.
There's a separate need: "may this caller touch *this specific event
type's* data at all," independently for publishing vs. reading, and
configurable per event type at registration time (e.g. a `PatientAdmitted`
event type might require a `clearance:phi` claim that most callers with
plain `events:publish`/`events:follow` scopes don't have).

Decision:
- `EventTypeDefinition` gains two optional fields,
  `RequiredPublishClaim`/`RequiredReadClaim` (`02-data-model.md`), each a
  single `"type:value"` claim string (e.g. `"clearance:secret"`) or `null`
  for no extra restriction. v1 supports exactly one required claim per
  direction — not an AND/OR set of claims.
- These are genuinely separate from each other, per explicit direction: a
  caller can be allowed to publish `PatientAdmitted` events without being
  allowed to read them back (or vice versa) — one claim does not imply the
  other.
- `RequiredPublishClaim` gates `POST /publish/{event-type}`.
  `RequiredReadClaim` gates `QUERY /follow/{event-type}` (`ADR-012`;
  checked once, at connect time) **and** all four Lineage API endpoints.
- **Visibility is per node, not per request: "you can only see what you can
  see."** For the Lineage API and the Follow envelope's `parentEventIds`
  alike, each event a response touches is checked independently against
  the caller's `RequiredReadClaim`. A node the caller can't see is
  **not** shown — not its `eventType`, `sequenceNumber`, `occurredAt`, or
  payload — but that does *not* fail the rest of the response: other
  nodes the caller *can* see are still returned. Lacking access to a
  parent never blocks access to a child the caller otherwise has rights
  to, and vice versa — the two are evaluated completely independently.
  `03-api-contracts.md`, "RequiredReadClaim and the Lineage API", has the
  concrete response shape.
  - The one exception is the **root** `{eventId}` a Lineage call names
    directly: that one must be visible to the caller or the whole request
    is rejected (`403`) — you cannot ask about the lineage of something
    you can't see at all. Everything the traversal *discovers* from there
    (parents, children, ancestors, descendants) is visibility-checked
    per node as above, not gated by the root's check.
  - Traversal does not recurse past a node the caller can't see, for the
    same reason it doesn't recurse past a `resolved: false` (Permissive
    dangling) node: nothing about what's beyond an invisible node is
    revealed either. Both are "leaves" to the caller, for related but
    distinct reasons — one because it doesn't exist yet, one because the
    caller isn't allowed to see it.
  - **This is also why publish never needed to check `RequiredReadClaim`
    on a referenced parent** (an earlier open question): read visibility
    is entirely a per-viewer, read-time decision, never baked in when the
    link is created. `ParentLinkService` (`ADR-005`) still only checks
    *existence*, regardless of who's publishing or who might later be
    unable to read that parent back.
- The check is enforced in application code after the event type is
  resolved from the registry, not as a static ASP.NET Core policy — see
  `06-solution-structure.md`. It requires the caller's claims to already be
  populated by JWT bearer auth (`ADR-006`), so it can't be enforced before
  that exists; see `08-build-plan.md`, Phase 6.
- Registering/changing these claims still only requires `registry:admin` —
  no new scope.

Consequences:
- Two independent knobs (publish vs. read) means an event type can be
  write-only-to-some, read-only-to-others, both, or neither — flexible, but
  it also means there are two places to get the claim wrong when
  registering a sensitive event type, not one.
- A caller who lacks `RequiredReadClaim` for the **root** event a Lineage
  call names directly, when that event **does** exist, gets `403`, not
  `404` — this deliberately leaks that *something* exists at that
  `eventId` (distinguishable from a truly unknown `eventId`, which is still
  `404`), rather than hiding existence the way returning `404` for both
  cases would. That's a conscious trade-off for consistency with how the
  scope-based `403`s already behave, not an oversight; revisit if this ever
  needs to defend against enumeration/existence-probing specifically. A
  node merely *discovered* during traversal, by contrast, is stubbed
  (`restricted: true`, per node, see above) rather than surfaced as a
  distinct status code — there's no equivalent "does this discovered node
  exist" question being asked directly, so there's nothing analogous to
  leak.
- The recursive CTE (`IEventLineageQueryProvider`,
  `06-solution-structure.md`) must enforce the stop-at-invisible-node rule
  *during* recursion, not just redact fields in the final output — a
  provider that fully expanded the graph and only masked the display would
  still silently reveal a restricted node's position and connectivity, the
  exact leak this design exists to prevent.
- Tightening a claim on a new schema version takes effect immediately for
  new requests, but does **not** retroactively affect an already-open
  Follow SSE connection (the check runs once at connect time) — a caller
  connected before the tightening keeps receiving events until they
  reconnect. If this window matters, closing live connections on a claim
  change would need to be a separate mechanism, not assumed to fall out of
  this design for free.
- This is a second, independent enforcement point that must be kept in sync
  with `ADR-006`'s scope checks conceptually (both must pass), but the two
  are implemented differently (static policy vs. per-request data lookup)
  — see `06-solution-structure.md` for why they can't share one mechanism.
- Property-level masking (`ADR-009`) is a finer-grained relative of this
  same idea, reusing the `"type:value"` claim string convention —
  intentionally, so the two features compose rather than inventing a second
  claim format later.
- **`RequiredPublishClaim`/`RequiredReadClaim`'s "exactly one claim per
  direction" limit is generalized to a list by `ADR-050`**, plus both
  are now guaranteed to be surfaced as an `x-required-claims` OpenAPI/
  AsyncAPI Specification Extension in generated docs, and reused to
  drive log redaction — see that ADR for the full shape.

**Compliance note** (a proving-ground compliance review, this session):
`RequiredPublishClaim`/`RequiredReadClaim`'s per-event-type, per-node
enforcement is the concrete mechanism implementing NIST SP 800-53 Rev.
5's `AC-3` ("Access Enforcement") — the same control family `ADR-046`'s
RBAC role indirection cites, layered on top of this ADR rather than
replacing it: this is the actual request-time enforcement point, roles
and direct grants (`ADR-046`) just decide which claims a caller ends up
holding by the time this check runs.
