[← Comparisons index](README.md)

# Multi-tenant isolation model: shared infrastructure vs. siloed-per-tenant

**Raised by:** `docs/10-open-questions.md`'s tenant-isolation row — this
design's own convention says a genuine multi-option fork earns a
comparison *before* the deciding ADR, and this one never got one despite
`ADR-030` (multi-tenancy), `ADR-034` (sharding), `ADR-058` (rate
limiting), and `ADR-061` (residency) all quietly assuming an answer.
Independently corroborated by a cross-reference against a separate
architecture document (this session), which found the same gap on both
sides: neither design stress-tests its assumption against a single
overloaded or compromised tenant.

**Direction received, this session:** dedicated infrastructure per
tenant, not shared — stated plainly as "servers are cheaper than
lawsuits... not worth the risk to have shared client infrastructure,"
with cross-tenant exchange handled by federated messages in and out,
never by shared storage or compute.

## The fork

Every multi-tenant system sits somewhere on a spectrum from "one shared
stack serving every customer" to "one fully separate stack per
customer." **Verified real terminology before naming anything** (this
project's standing convention):

- **Pool / shared model** — every tenant's data lives in the same
  running stack, distinguished by a scoping key. [Azure Architecture
  Center's tenancy-models guide](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/considerations/tenancy-models)
  calls the fully-shared end of this spectrum the **pool model**.
  `ADR-030`'s original framing (`AppId` as a scoping key inside shared
  stores) is exactly this.
- **Silo model** — each tenant gets a fully separate, dedicated
  infrastructure stack: no shared compute, no shared storage, nothing.
  Named explicitly by both real sources checked: [AWS Well-Architected
  SaaS Lens — Silo Isolation](https://docs.aws.amazon.com/wellarchitected/latest/saas-lens/silo-isolation.html)
  ("each tenant is running a fully siloed stack of resources") and
  Azure's Architecture Center, which calls the automated version of this
  **"Automated single-tenant deployments"** (via the [Deployment Stamps
  pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/deployment-stamp)).
- **Bridge model** — a hybrid: some components (often the control
  plane/gateway) shared, the tenant-specific data plane isolated. Named
  by the same Azure guide as the middle of the spectrum.

## What the real sources say drives the choice

Checked directly rather than assumed:

- **AWS's own stated rationale for Silo** maps directly onto this
  session's reasoning: *"supporting challenging compliance models"*
  (regulated environments with strict isolation requirements), *"no
  noisy neighbor concerns,"* and — the explicit blast-radius framing —
  *"any failures that occur within a given tenant's environment will
  likely be constrained to that environment... the error cannot cascade
  through the remaining tenants."* Its stated cost: *"trades economies
  of scale and operational efficiency for compliance, business, or
  domain considerations."*
- **Microsoft's own stated cost is blunt**: *"cost efficiency is low...
  if a single tenant requires a specific infrastructure cost, 100
  tenants probably require 100 times that cost."*
- **A real, quantified link between infrastructure isolation and breach
  cost** (not identical to multi-tenant blast radius, but related and
  worth citing honestly as such): IBM's [Cost of a Data Breach
  Report](https://www.ibm.com/reports/data-breach) found more isolated
  (hybrid-cloud) environments cost 28.3% less per breach than
  public-cloud/shared environments, and multi-environment breaches take
  longest to contain.
- **One real counter-example checked, not assumed**: Veeva Vault (the
  dominant clinical-trials eTMF/CTMS platform, 21 CFR Part 11-compliant)
  is verified to be architected as **genuinely multi-tenant** (pool
  model, with "Vault instances" as a *logical*, not
  infrastructure-per-tenant, isolation unit) — proof that regulated
  compliance status alone doesn't force Silo. The choice here is a
  deliberate risk-tolerance call, not something 21 CFR Part 11 itself
  mandates.

## Federation between siloed deployments — verified, not invented

If tenants never share infrastructure, cross-tenant exchange (a
multi-site clinical trial's sponsor deployment needing a site's data; a
KYC relying party needing another institution's attestation) needs its
own answer. Two real, named patterns exist for exactly this — verified
before citing:

- **AS2 / AS4** (Applicability Statement 2/4 — HTTP(S)+S/MIME and
  OASIS ebMS3-based B2B transports, respectively) — the dominant EDI
  mechanisms for independently-owned trading-partner systems to exchange
  documents point-to-point with **no shared database**.
- **Federated architecture** (health-information-exchange terminology)
  — each institution keeps its data at home; an interoperability
  gateway plus standard APIs (FHIR, in HIE literature) let institutions
  exchange specific records on request, never via a central shared
  repository.

**This design already has the exact primitives either pattern needs —
nothing new to build**: `ADR-060`'s outbound webhooks (signed,
retriable, Standard-Webhooks-conformant) are the outbound half; `ADR-072`'s
`IInterchangeFormatAdapter` (already built for HL7v2/FHIR/ICH E2B(R3)/
GS1-EPCIS) is the inbound half. Treating a sibling tenant's dedicated
deployment as *just another external system to federate with*, using
mechanisms already built for federating with hospitals/regulators/supply-
chain partners, is more consistent than inventing a second,
tenant-to-tenant-specific exchange mechanism.

## Recommendation

**Silo (dedicated deployment per tenant) as the standard model,
federated via `ADR-060`/`ADR-072` — not the pool model `ADR-030`
originally assumed.** This is the decision `ADR-075` formalizes. Argued
directly against the real trade-off named above, not by ignoring it:
the operational cost multiplier AWS/Microsoft both name explicitly is
accepted, deliberately, because the stated liability/blast-radius risk
of shared client infrastructure outweighs it — the same calculus AWS's
own Silo-isolation guidance names as the reason regulated workloads pick
it. `ADR-033`'s peer-sync mesh and `ADR-034`'s entity-type sharding are
**not discarded** — they remain exactly what a *single* tenant's own
dedicated deployment uses internally, for its own multi-site fault
tolerance or its own scale, respectively; what changes is that they no
longer span *across* different customers.
