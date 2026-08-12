[← ADR index](../07-adrs.md)

# ADR-075: Siloed, dedicated-per-tenant deployment — federated via existing interchange/webhook mechanisms, never shared infrastructure

Status: Accepted — revises `ADR-030`

Context: `ADR-030` made `AppId` a first-class scoping key so multiple
independent applications could share one running deployment of this
framework's core engine — the **pool model**, in verified real
terminology (`docs/comparisons/multi-tenant-isolation-model.md`,
[Azure Architecture Center's tenancy-models
guide](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/considerations/tenancy-models)).
This was never weighed against the real alternative before being
assumed — `docs/10-open-questions.md`'s tenant-isolation row named this
gap directly, and an independent cross-reference against a separate
architecture document found the identical gap on both sides
independently. Direction received this session: dedicated infrastructure
per tenant, not shared — stated plainly as "servers are cheaper than
lawsuits... not worth the risk to have shared client infrastructure,"
with cross-tenant exchange handled by federated messages, never by
shared storage or compute. See the comparison doc for the full
pros/cons and the real sources checked (AWS Well-Architected SaaS
Lens's **Silo Isolation** page; Azure's **Automated single-tenant
deployments**) before this decision.

Decision:
- **Silo is now the standard deployment model**: every tenant gets a
  fully separate, dedicated stack — its own compute, its own storage,
  nothing shared with any other tenant's deployment, ever. This is a
  deployment-topology decision, not a core-engine rewrite — the engine
  itself remains exactly as domain-agnostic as `ADR-030` already
  established; what changes is that "one running instance serves many
  customers" is no longer the assumption.
- **`AppId` is not removed — its scope narrows.** Within *one* tenant's
  own dedicated deployment, `AppId` remains exactly as useful as
  `ADR-030` designed it: a tenant that itself runs multiple internal
  applications on its own stack still gets `ADR-030`'s zero-collision,
  independently-versioned registry per application. What `AppId` no
  longer means is "which of several different *customers* sharing this
  running system owns this data" — that boundary is now the deployment
  itself, not a scoping key inside a shared one.
- **`ADR-033`'s peer-sync gossip mesh and `ADR-034`'s entity-type
  sharding are unchanged in mechanism, narrowed in scope.** Both
  remain exactly what they already were — a durable, resumable
  replication primitive and a sharding strategy — but now understood to
  operate *within* one tenant's own dedicated, possibly multi-site
  deployment (a hospital system replicating across its own three data
  centers), never *across* different tenants' deployments. Nothing
  about either mechanism needs to change; only the boundary they
  operate inside does.
- **Cross-tenant exchange is federation via already-adopted mechanisms,
  not a new one.** A sibling tenant's dedicated deployment is treated as
  *just another external system to federate with* — the same relationship
  this design already has with a hospital EMR or a regulator, not a
  special case. Outbound: `ADR-060`'s webhook dispatcher (signed,
  retriable, Standard-Webhooks-conformant). Inbound: `ADR-072`'s
  `IInterchangeFormatAdapter` seam (already built for HL7v2/FHIR/
  ICH E2B(R3)/GS1-EPCIS). Real, verified prior art for this exact shape
  — independently-owned systems exchanging documents with no shared
  database — checked before deciding: **AS2/AS4** (B2B EDI transport)
  and health-information-exchange-style **federated architecture**
  (each institution keeps its own data, exchanges specific records via
  standard APIs, never a central repository). No new protocol invented.
  **Honest residual, not fully solved — flagged by a buildability
  review this session, not glossed over**: `ADR-072`'s adapters were
  built for *externally standardized* formats (HL7v2, FHIR, ICH E2B(R3),
  GS1-EPCIS) — each side of that mapping is anchored to a real published
  spec neither party controls. A sibling tenant emits this framework's
  own *native* event shape instead, from an independently-versioned
  schema registry (per tenant, by design) — there is no external spec
  to anchor a tenant-to-tenant mapping to, and a bespoke adapter per
  tenant pair doesn't scale past a handful of federation partners.
  ~~Tracked as an open question (`docs/10-open-questions.md`), not
  resolved here.~~ **Corrected, 2026-08-12, found by an independent
  design-compliance audit**: this residual was resolved by `ADR-082`
  ("Tenant-to-Tenant Federation Mapping") — accepted as ordinary
  `client_credentials` API calls with bespoke-per-pair shape mapping, no
  new adapter category, the same non-scaling trade-off this paragraph
  already named as the open question's own concern. The corresponding
  row no longer exists in `docs/10-open-questions.md` — it was deleted
  outright per that file's own resolution convention, with `ADR-082`
  itself standing as the permanent record.
- **The silo spectrum runs all the way down to a single machine — no new
  mechanism needed for that extreme either.** A "radius zero,"
  origin-authoritative deployment (one standalone install, database on
  the client's own hardware, zero replication peers) is already fully
  supported by existing choices, not a new deployment shape to design:
  `ADR-001`'s SQLite provider already exists for exactly this
  no-real-infrastructure case, and `ADR-051`'s peer list is simply empty
  for a node with no replication partners at all. The silo model's range
  spans from this one-machine extreme up to a multi-site enterprise
  deployment (`ADR-033`'s mesh, scoped to that one tenant) — the same
  mechanism at every point on the range, just with zero-to-many
  configured peers.
- **The external ingress/gateway layer (`ADR-049`, YARP) may still be a
  shared, stateless routing tier** — TLS termination and request routing
  to the correct tenant's dedicated backend, holding no tenant data
  itself — the same "bridge model" partial-sharing Azure's own guidance
  names as compatible with Silo tenant-data isolation. Flagged as a
  judgment call, not a hard requirement: a deployment with an even
  stricter isolation bar may run a fully separate gateway per tenant
  too: `ADR-049`'s own text doesn't need to change either way.

Consequences:
- **Real, accepted operational cost, stated plainly rather than
  glossed over** — both real sources checked name this explicitly
  (Microsoft: "100 tenants probably require 100 times that cost"; AWS:
  "trades economies of scale... for compliance, business, or domain
  considerations"). This design accepts that cost deliberately, for the
  liability/blast-radius reason stated in the comparison doc, not
  because the cost was overlooked.
- **`ADR-058`'s per-tenant rate limiting changes purpose, not
  mechanism** — it no longer protects one tenant's traffic from another
  tenant's noisy neighbor effect (siloed deployments have no shared
  compute to contend over); it now protects a tenant's own dedicated
  deployment from its own runaway callers/applications. The mechanism
  (`ADR-058`'s partitioned rate limiter) is unaffected.
- **`ADR-061`'s data-residency enforcement simplifies for the common
  case** — a tenant requiring a specific region simply has its entire
  dedicated stack deployed there; `AllowedRegions`-filtered peer-sync
  still matters for a tenant whose *own* multi-site deployment spans
  regions, just no longer needs to reason about *other* tenants' peers
  at all.
- **`ADR-056`'s PITR-restore concern (raised in the open question this
  ADR resolves) is now moot for the common case** — restoring one
  tenant's own dedicated deployment naturally restores only that
  tenant, since there is no shared store to restore selectively from in
  the first place.
- **Corrected, later pass: propagated.** `06-solution-structure.md`'s
  deployment-unit description, `08-build-plan.md`'s item text, and
  `01-c4-architecture.md`'s container diagram all now correctly describe
  the silo model, not the pool-model assumption this note originally
  flagged as outstanding — confirmed directly by a design-compliance
  audit re-checking this claim against the current files, not assumed.
- **`ADR-030` is revised in place for the specific "many applications
  in one shared deployment" framing** — struck through there per this
  project's additive-history convention, not deleted; everything else
  in `ADR-030` (the registry-key shape, the zero-domain-knowledge core
  engine rule) is unaffected and still Accepted as originally written.

**Compliance note**: directly satisfies the same SOC 2/ISO 27001
logical-segregation-of-customer-data control `ADR-030`'s own compliance
note already named — Silo satisfies it structurally (separate
infrastructure) rather than logically (a scoping key inside shared
infrastructure), a stronger, not weaker, form of the same control.
