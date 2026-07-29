[← ADR index](../07-adrs.md)

# ADR-046: Role-Based Access Control — permissions granted to roles, roles assigned to users

Status: Accepted

Context: Every claim/permission this design has introduced so far
(`RequiredPublishClaim`/`RequiredReadClaim`, `ADR-008`; delegated grants,
`ADR-043`; application-defined permission types, `ADR-044`) is assigned
**directly** — a user either has a given claim or doesn't, with no
indirection between "what a user can do" and "who that user is."
Direction received this session: introduce that indirection — permissions
should be granted to roles, and roles assigned to users, the standard
shape almost every real identity system uses once direct claim
assignment stops scaling.

This is **Role-Based Access Control (RBAC)**, formalized by Ferraiolo &
Kuhn at NIST (1992) and standardized as
[ANSI/INCITS 359](https://blog.ansi.org/ansi/role-based-access-control-rbac-incits-359/)
(2004, revised 2012/R2022) — not a bespoke idea. The standard's own
Reference Model separates **user-role assignment** from
**role-permission assignment** as two independent relations, which maps
directly onto this design's own existing separation of concerns: this
ADR only needs to add the missing indirection layer, not touch how a
permission is *checked* at all.

Decision:
- **`Role` is a new, `AppId`-scoped registry concept**
  (`docs/data/schema-registry.md`, alongside `ADR-044`'s `AppTrustRoot`):
  `Role { AppId, RoleName, Permissions: [claim/scope strings] }` — a
  named bundle of the exact same opaque permission strings this design
  already uses everywhere (`ADR-008`'s claims, `ADR-006`'s scopes,
  `ADR-044`'s application-defined permission types). The core engine
  still never validates *what* a permission string means — only that a
  role bundles a set of them, the same "opaque to the framework" posture
  every claim already has.
- **Role-to-permission expansion happens once, at token issuance —
  not on every request.** The identity provider resolves a user's
  assigned role(s) into the flattened union of each role's permissions
  and bakes that union directly into the issued JWT's claims, exactly
  the shape every claim-check in this design already expects. **No
  existing check changes**: `ADR-008`'s `ClaimsPrincipal.HasClaim(type,
  value)`, `ADR-043`'s entity-scoped claim check, and `ADR-044`'s
  `AppTrustRoot`-rooted UCAN validation are all completely unaware
  whether a claim arrived via direct assignment or via a role — RBAC is
  purely an issuance-time indirection layered underneath.
- **Role *assignment* (which user has which role) is identity-provider
  state, not an event-sourced core-engine concern** — the same scoping
  `ADR-006` already drew around seeded dev clients ("the production IdP
  remains a separate, later decision, out of scope for this POC").
  `EventStore.DevIdp`'s dev-mode seeding gains role-to-permission and
  user-to-role tables alongside its existing seeded clients; a real
  production IdP would own this the same way it would own user accounts
  generally.
- **Roles compose with what already exists, no new mechanism needed
  for either**: a delegated grant (`ADR-043`) can delegate an entire
  role, not just a single claim — same UCAN capped-delegation shape,
  since "assume role X, time-boxed, capped at what I hold" is no
  different in kind from "have claim Y, time-boxed." An application's
  own custom permission types (`ADR-044`) are exactly what an
  application-defined `Role` would most naturally bundle — "Attending
  Physician" or "Records Clerk" are job functions an application
  defines, composed from permissions it also defines.
- **Direct, per-user permission assignment also exists, alongside
  roles — but it is additive-only, never restrictive.** A user's
  effective permission set is always the **union** of every permission
  granted by their assigned role(s) *and* any permissions assigned
  directly to them — there is no mechanism anywhere in this model for a
  direct assignment (or a role) to *subtract from* or *override* a
  permission granted some other way. This is a deliberate simplification,
  not an oversight: allow/deny precedence rules (which grant wins when
  two sources disagree) are a well-known, well-documented source of
  policy bugs in real systems; a strictly additive-only model has no
  such conflict to resolve in the first place, because there is nothing
  to resolve — every source is a positive grant, full stop. This is
  resolved the same way role expansion is: the IdP unions role
  permissions and direct permissions into one flattened claim set at
  token issuance, and every downstream check still just asks "is this
  claim present," unaware of which source it came from.

**A directly-assigned user permission is real state, not
merely a delegated grant reused (`ADR-043`) or a role reused (above)**
— it needs its own IdP-side record, `UserPermission { ActorId, AppId,
Permission }`, alongside the role-assignment state `ADR-046` already
places in identity-provider scope, not the core engine.

Consequences:
- `docs/data/schema-registry.md` gains the `Role` entity — done this
  pass. `UserPermission` is identity-provider state (like role
  assignment itself), not a schema-registry entity — no core-engine
  data model change for it.
- **No explicit-deny/negative-permission concept exists anywhere in
  this model, by design** — consistent with the additive-only decision
  above. An application that genuinely needs to revoke a specific
  permission from a specific user does so by not assigning it (or not
  assigning the role that grants it) in the first place, or by
  `ADR-043`'s grant revocation for a temporary delegation specifically —
  never by adding a "deny" entry that would have to out-rank some other
  grant.
- **A role hierarchy (roles inheriting other roles' permissions) is
  explicitly not adopted** — `ANSI/INCITS 359` defines this as an
  optional, more advanced tier of the standard ("Hierarchical RBAC");
  this design adopts only the base "flat roles bundle permissions"
  tier, since nothing here has yet needed the added complexity of role
  hierarchies. Revisit only if a real need for one shows up, not
  preemptively.
- **Static/dynamic separation-of-duty constraints** (also part of the
  fuller RBAC standard — e.g. "a user can never hold both Role A and
  Role B simultaneously") are likewise not adopted here — no current
  requirement calls for them; flagged so a future reader knows this was
  a deliberate scope line, not an oversight.
- Auditing which role a reader's claims came from is a natural extension
  of `ADR-045`'s `AccessLog` (`ReaderTrustBasis` could note "via role
  X") — not designed further here, left as a natural, low-cost future
  enrichment rather than a required change to `ADR-045`'s shape.
