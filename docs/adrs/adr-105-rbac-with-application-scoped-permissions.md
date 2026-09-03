[← ADR index](../07-adrs.md)

# ADR-105: RBAC with application-scoped permissions — generalized roles as JWT claims, permission expansion via STS exchange or middleware

Status: Accepted

Context: `docs/comparisons/authorization-model.md` surveyed six patterns
(RBAC-extended, ABAC/policy-based, ReBAC/relationship-tuple, a named
Hybrid, DACL, Classification-based/MAC) against a shared scenario, plus
a separate "Application-owned local authorization STS" direction (a
central identity layer issuing only generalized roles; each application
mapping them to its own permissions). That comparison's own Recommendation
leaned Hybrid. Direct request decides differently — this ADR is that
decision, citing the comparison rather than re-deriving its analysis:
"grants should be validated on the server ti[m]e to check for a
revocation[; resolved separately by `ADR-104`]. DID/UCAN would be nice.
I would like rbac with permissions. the rbac roles could be claims on
the jwt and the permissions could either be upgraded on demand with an
sts exchange or just added on the client side in the middleware."

Decision:
- **RBAC (the comparison's Option A), not ABAC/ReBAC/Hybrid/DACL/
  Classification, is Duplex's chosen decision model.** A central
  identity layer issues **generalized, cross-application roles** as
  ordinary JWT claims (e.g. `roles: ["clinician"]`) — this is the
  "generalized-role layer" the comparison's STS section identified as
  the piece missing from today's system, where every claim/role is
  already application-specific from the moment it's issued.
- **Each deployed application expands a generalized role into its own
  entity-type × access-level permissions by one of two equally
  legitimate mechanisms, chosen per application, not mandated
  platform-wide**:
  1. **An RFC 8693 Token Exchange step** (already adopted, `ADR-036`'s
     mechanism) — the application's own local STS trades the
     generalized-role token for a new, app-scoped access token carrying
     the expanded permission claims. Matches the comparison's own STS
     section precisely (Okta's per-API custom authorization servers,
     Kubernetes `ClusterRole`+per-namespace `RoleBinding` as the
     verified real precedents).
  2. **A client-side/Gateway middleware expansion** — the generalized
     role claim is expanded into permission context in-process (no
     token round-trip), by middleware sitting in front of the
     application's own endpoints (`EventStore.Gateway`, `ADR-049`, is
     the natural home for this if built there rather than per-Host).
  Both are real, deployment-time choices — an application picks
  whichever fits its own latency/complexity trade-off; this decision
  does not pick one over the other, per direct request ("could either...
  or just...").
- **Reuses this design's existing, already-config-driven pipeline for
  the role/permission-grant mechanism itself** (`ADR-046`/`067`) — a
  role or permission grant is still a real, hash-chained reserved event
  published through `EventStore.Rbac`'s scope-gated API, folded by
  `RbacProjectionWorker`, exactly as today. This decision changes *what*
  gets granted (a generalized role, one layer up from today's already-
  app-specific roles) and *how it gets expanded* (STS or middleware,
  above) — not the grant-issuance mechanism itself.
- **The registration-helper promotion already tracked in `TODO.md`**
  (found during Phase 3, cross-domain review: `VitalsSharedTypes.cs`/
  `MeridianSharedTypes.cs`'s duplicated `EnsureAuthorityDecisionRegisteredAsync`)
  is the natural structural fit for the role→permission mapping code
  this decision implies, once built — cross-referenced, not re-decided
  here.

**Explicitly out of scope for this decision, not silently dropped**:
the comparison's own shared scenario was built around a **per-user-task**
grant (a specific Vitals PI resolving one specific assigned
`AdverseEventReported` task, not every task in the trial) — pure RBAC,
as decided above, does not natively express that granularity; a role
says "clinician," not "clinician for *this* task." Direct request did
not ask for this specific case to be solved by this decision, so it
isn't — if per-task granularity is still needed, the comparison's own
Option F (Hybrid) already sketches the fit: a small, Zanzibar-shaped
tuple table (`TaskGrant { EntityType, EntityId, Relation, ActorId }`)
layered *on top of* the RBAC decision made here, not a reason to revisit
it. This is a real gap in what this ADR covers, named honestly rather
than assumed solved by "permissions" being a broad enough word.

**Prior art**: `docs/comparisons/authorization-model.md`'s own six-option
survey and its "Application-owned local authorization STS" section
(Okta custom authorization servers, Kubernetes `ClusterRole`/
`RoleBinding`, RFC 8693, RFC 9396, SCIM, RFC 7591/7592/8414) — all
already verified there; not re-verified here.

Consequences:
- **Closes `TODO.md`'s "Decide the authorization model" item.** Add a
  row to `docs/comparisons/README.md`'s catalog: this ADR decides
  differently than that comparison's own Recommendation (Hybrid) —
  stated explicitly, not hidden, since the comparison's Recommendation
  is a proposal for the user to weigh, not a binding outcome, and this
  ADR is the actual, direct-request decision.
- **The generalized-role layer above today's per-`AppId` roles is new
  work, not yet built** — today's DevIdp seeds only already-app-specific
  roles/claims (`vitals-pi-client`'s `review:ae`, etc.); nothing issues a
  bare, cross-application `"clinician"` role today. This ADR decides the
  shape; implementing it (a real generalized-role claim, a real STS-or-
  middleware expansion step) is future code work, out of scope for this
  design-phase-only session.
- **`docs/comparisons/authorization-model.md` itself is unaffected** —
  its six-option analysis and worked scenario remain accurate and
  reusable (e.g. if the per-task gap above is ever addressed via Option
  F); only its own Recommendation is the part this ADR diverges from,
  noted in place per the additive-history convention rather than
  editing that comparison's own text to match this decision after the
  fact.
- **This is a framework-level (Duplex) decision, not domain-specific** —
  applies identically to Vitals and Meridian, and to any future
  application built on this platform, consistent with `ADR-030`'s own
  zero-domain-knowledge-in-the-core-engine posture.
- Resolves `TODO.md`'s "Decide the authorization model" item outright
  (deleted from that file per its own workflow); the OIDC/OAuth2 scope
  item and the OpenID Federation item remain separately open — this
  decision doesn't resolve either, it only decides the *permission
  model* the generalized-role token would eventually carry.
