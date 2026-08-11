[← ADR index](../07-adrs.md)

# ADR-067: Control-plane actions are reserved event types in the same Event Log, not a separate audit table

Status: Accepted

Context: `docs/10-open-questions.md` asked whether control-plane/
administrative actions — schema registration (`ADR-020`), RBAC role/
permission grants (`ADR-046`), `AppTrustRoot` registration (`ADR-044`) —
get the same audit rigor as ordinary business events, or are plain CRUD
with no equivalent trail. Direction received this session: **audit
actions could be linked to other events, so it makes sense to keep them
in a single store, as their own event types** — resolving in favor of
modeling them as ordinary, reserved events in the *same* Event Log,
not a separate table.

Decision:
- **Every control-plane mutation publishes a reserved, platform-level
  event** — `SchemaRegistered`, `RoleGranted`/`RoleRevoked`/
  `PermissionGranted` (`ADR-046`), `AppTrustRootRegistered` (`ADR-044`),
  and by the same principle any future administrative mutation this
  design adds. **Reserved the same way `ADR-020`'s `EventUpcastFailed`
  already is** — an operator never registers these via `PUT /registry/
  {event-type}`; they're built into the platform, not schema-author-
  defined. No new reservation mechanism, reusing the one `ADR-020`
  already established.
- **Same store, same `StoredEvent` shape, same hash chain** — these
  events get `ActorId` (`ADR-064`, who made the change), can carry
  `Signature` (`ADR-066`, if a specific control-plane action is
  configured to require sign-off — granting a high-privilege role is a
  plausible candidate), and fold into `ADR-019`'s tamper-evident chain
  automatically. No new durability or tamper-evidence mechanism needed —
  this is the entire point of reusing the existing store rather than
  building a parallel one.
- **The existing `EntityId` convention applies unchanged** —
  `{appId}:{entityType}:{uniqueId}` (`ADR-021`), e.g.
  `{appId}:schema:{eventType}:{version}` or ~~`{appId}:role:{roleId}`~~ —
  control-plane data is already `AppId`-scoped (`EventTypeDefinition`,
  `AppTrustRoot`, RBAC roles all are), so no new identity scheme is
  needed to fit it into the existing model.
  **Corrected, later pass**: `{appId}:role:{roleId}` turned out not to
  fit `RoleGranted`/`PermissionGranted` once actually built (`docs/08-
  build-plan.md` item 30, "Control-Plane Actions as Reserved Events") —
  that shape assumes one mutable record per role, but a role/permission
  can be granted to many actors independently, and the generic
  `EntityStoreRow` patch-merge fold can only ever mean "replace field X,"
  never "add/remove one item from a set," so multiple actors' grants of
  the same role would silently overwrite each other under that key. The
  real implementation ([`RoleGrantedEventType.cs`](../../src/EventStore.Rbac/RoleGrantedEventType.cs),
  [`PermissionGrantedEventType.cs`](../../src/EventStore.Rbac/PermissionGrantedEventType.cs))
  uses a synthetic composite key instead, computed by the publishing
  endpoint (not resolved from a single JSON pointer): `actorId:roleName`
  for `RoleGranted`/`RoleRevoked`, `actorId:permission` for
  `PermissionGranted`. `AppTrustRootRegistered` is unaffected and fits
  the literal `{appId}:...` example directly (one record per
  `(AppId, IssuerDid)`).
- **The CRUD-shaped tables this design already has (`EventTypeDefinition`,
  `AppTrustRoot`, RBAC's `Role`/`UserPermission`) become current-state
  read models, folded from these events — the same relationship
  `EntityStoreRow` already has to business events** (`ADR-021`). This
  isn't a new pattern grafted on; it's the same write/read split this
  entire design already demonstrates, now applied to the framework's own
  control plane instead of only tenant business data.
- **Linkable via the existing `parentEventIds` lineage mechanism
  (`ADR-005`), where a genuine causal relationship exists to a specific
  business event** — e.g., a business event published under a
  particular RBAC grant can name that grant's event as a parent, letting
  the existing Lineage API trace "this action was taken under
  permissions granted by this specific event" using traversal machinery
  this design already has. Not a blanket requirement that every business
  event link back to its schema registration (that relationship is
  already captured by the existing `SchemaVersion` field — lineage isn't
  needed to duplicate it); linking is used where it adds a real causal
  story a plain foreign key doesn't already tell.
- **Explicitly not `ADR-045`'s separate-`AccessLog` shape, and here's
  why the two decisions differ rather than contradict**: `ADR-045`'s
  read access log is separate because reads vastly outnumber writes (a
  genuinely different volume/performance profile) and because linking a
  read to the *business* event it read would be meaningless (a read
  doesn't cause anything). Control-plane mutations are writes,
  structurally identical in shape and volume to ordinary business
  events, and specifically benefit from participating in the same
  lineage DAG business events do — the opposite profile, warranting the
  opposite storage choice.

**Compliance note** (a proving-ground compliance review, this session):
this ADR is the specific mechanism satisfying **SOX Section 404**'s
change-management IT General Control — "who changed what configuration/
permission, when" for schema registrations, RBAC grants, and trust-root
registrations is exactly what modeling these as ordinary, hash-chained
events (`ADR-019`) already gives for free. A confirming non-gap for the
brokerage proving-ground candidate, alongside `ADR-045`'s access-control
ITGC and `ADR-071`'s SEC 17a-4 finding — no new mechanism needed here
either.

Consequences:
- Resolves `docs/10-open-questions.md`'s control-plane-audit row — the
  table is now fully empty again.
- No new store, no new tamper-evidence primitive, no new identity
  scheme — this ADR is entirely a reuse of `ADR-005`/`ADR-019`/
  `ADR-020`/`ADR-021`'s existing mechanisms applied to a set of triggers
  (schema/RBAC/trust-root mutation) that previously had no equivalent
  trail at all.
- `docs/data/schema-registry.md`'s `EventTypeDefinition`/`AppTrustRoot`/
  RBAC entities gain a documented relationship to their folding reserved
  events — flagged as remaining propagation work (the reserved event
  payload shapes themselves are not fully specified this pass).
- A schema/role/trust-root read is now folded from a WRITE path that
  looks identical to the business-event write path — a hosting team
  extending this framework via `docs/extensibility-points.md` doesn't
  need to learn a second mutation pattern for administrative data.
