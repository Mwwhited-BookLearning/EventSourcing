[← Comparisons index](README.md)

# Choosing a Proving-Ground Domain

**Decided: both leading candidates, not a single pick.** Raised by a
generalized-framework review (this session): this design had no real
domain built against it yet beyond the small, deliberately generic
`Orders` CQRS worked example (`ADR-030`'s own framing: "a sample
application, not part of the core engine"). Direction received:
building **two** proving-ground applications — **clinical trials +
connected medical-device telemetry** and **digital identity/KYC** —
rather than one, explicitly because two domains give materially better
feature coverage (the matrix below shows neither domain alone reaches H
on every mechanism, while the pair does) *and* reduces the risk of this
framework reading as pigeonholed into one industry, which a single
domain — however broad its coverage — would still risk implying.
`ADR-030`'s multi-tenant, domain-agnostic core is exactly what makes
running two proving-ground domains side by side a real, low-cost option
rather than a second framework, per the Recommendation below (written
before this decision, kept as the reasoning that led to it).

**See `docs/domains/README.md` for the per-domain reference generated
from this comparison's coverage matrix and regulatory mapping table**
below — one file per domain considered here (all 15), covering which
ADRs apply and why, governing regulations, and special concerns; this
doc is where the *choice* was made, those files are the per-domain
reference for afterward.

**A follow-up review (this session), checking the *other* candidates
below for requirements the two chosen domains don't exercise**, found
one genuine, actionable gap and one confirming non-gap: brokerage/
capital-markets' payment-card handling surfaced `ADR-071` (PCI-DSS
Sensitive Authentication Data can't be registered as a schema field at
all — a hard boundary neither of the chosen domains needed since
neither directly touches raw card data), and its recordkeeping
requirement (SEC Rule 17a-4) turned out to already be satisfied by
`ADR-019`'s existing hash-chained log, no gap at all. Framework-level
fixes land regardless of which domains get built, per `ADR-030`'s own
"domain-agnostic core" reasoning — this is exactly why the check was
worth doing even though brokerage wasn't chosen.

## What "good coverage" means here

Not every domain needs to touch every ADR — that would be an artificial
target, not a real one. The useful question is which domains make each
ADR **load-bearing** rather than **decorative**: a domain where
`ADR-035`'s non-authoritative capture is genuinely how the domain already
works (a lab result pending physician sign-off) proves the mechanism far
better than one where it has to be bolted on to justify using it.

**Redone at full granularity, per direct request — every ADR, not a
curated subset of named mechanisms.** The original matrix bundled
related ADRs into named mechanisms (e.g. "RBAC + row-level security
(`ADR-046`/`ADR-043`)") and quietly excluded anything judged "already
universal." Both shortcuts are gone below: every one of the 72 ADRs is
addressed, with its own row wherever domain genuinely changes the
answer, or an explicit one-line note in the **universal/infrastructure**
list otherwise — stated as a deliberate category, not a silent omission.

## Which ADRs don't differentiate by domain (universal/infrastructure)

These 46 ADRs are framework-level decisions that apply the same way
regardless of which domain sits on top — forcing 15 near-identical
scores per row would be noise, not signal. Listed here once, by number,
so "all 72 ADRs" is genuinely accounted for without an unreadable table:

`ADR-001` (per-deployment DB provider, build-time choice) ·
`ADR-002` (on-demand spec generation) ·
`ADR-003` (reject unfilterable fields, superseded by `037`) ·
`ADR-004` (portable JSON columns) ·
`ADR-006` (dev OAuth/OIDC) ·
`ADR-008` (event-type security baseline — every domain needs *some*
claim gate; `009`/`046` are where domain actually differentiates) ·
`ADR-010` (tail/replay mode) ·
`ADR-011` (publish idempotency) ·
`ADR-012` (HTTP `QUERY` method) ·
`ADR-013` (Problem Details) ·
`ADR-014` (CORS policy) ·
`ADR-015`/`016` (CQRS projections, `ChangeKind`/merge) ·
`ADR-017` (DPoP-bound tokens) ·
`ADR-018` (event upcasting) ·
`ADR-020` (`SchemaVersion` on publish) ·
`ADR-021` (entity concept) ·
`ADR-022` (`Optional<T>` patches) ·
`ADR-023` (persist-everything) ·
`ADR-024` (optimistic concurrency) ·
`ADR-025` (API docs UI) ·
`ADR-026` (Aspire/OTel dev, Compose prod) ·
`ADR-027` (materialized upcasts) ·
`ADR-028` (downcast on retrieval) ·
`ADR-029` (logical-order fold) ·
`ADR-037` (GraphQL query layer) ·
`ADR-038` (compatibility/deployment discipline) ·
`ADR-039` (MVVM client architecture) ·
`ADR-040` (ticket exchange for headerless clients) ·
`ADR-041` (explicit composition/first-party libraries) ·
`ADR-044` (`AppTrustRoot` — mechanical extension of `036`/`043`) ·
`ADR-047` (claims augmentation for federated IdPs — extends `046`) ·
`ADR-048` (SPIFFE/SPIRE service identity — internal, not domain-facing) ·
`ADR-049` (API Gateway/YARP) ·
`ADR-050` (masking-as-spec-extensions + log redaction — inherits
`ADR-009`'s own domain relevance, scored there) ·
`ADR-051` (static peer-discovery seed list) ·
`ADR-052` (streaming-channel redaction — inherits `ADR-031`'s domain
relevance, scored there) ·
`ADR-053` (pluggable upcast engine) ·
`ADR-054` (client SDK generation) ·
`ADR-055` (testing strategy) ·
`ADR-056` (data lifecycle/backup design) ·
`ADR-059` (extensibility model) ·
`ADR-062` (package distribution) ·
`ADR-063` (staged distributed-correctness testing) ·
`ADR-064` (`ActorId` — foundational everywhere equally; `ADR-066` is
where signature-heavy domains actually differentiate) ·
`ADR-067` (control-plane actions as reserved events — every domain
benefits equally from an administrative audit trail).

## Full coverage matrix — every domain-differentiating ADR (26), all 15 domains

**H** = the domain's real-world workflow makes this ADR load-bearing.
**M** = fits and would get exercised, but isn't the domain's defining
characteristic. **L** = technically applicable, but contrived. **—** =
no realistic fit. Columns: **CT** clinical trials+device telemetry,
**KYC** digital identity/KYC, **IoT** industrial IoT, **INS** insurance+
telematics, **LOG** logistics, **BRK** brokerage/capital markets, **EDU**
education/credentials, **UTIL** utilities/smart metering, **PV**
pharmacovigilance, **BIO** biobanking, **PH** public health surveillance,
**ITAR** export-controlled defense data, **GOV** government case
management, **FOR** digital forensics/evidence custody, **DSCSA** pharma
supply chain. **Bold** = the single strongest fit found for that ADR
across all 15.

| ADR | CT | KYC | IoT | INS | LOG | BRK | EDU | UTIL | PV | BIO | PH | ITAR | GOV | FOR | DSCSA |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `005` Event lineage/DAG | H | M | H | M | **H** | H | L | M | H | H | M | H | M | H | H |
| `007` Derived/materialized events (deferred) | M | L | **H** | M | M | H | L | M | H | M | M | M | L–M | M | L |
| `009` Property-level masking | H | H | L | H | L | H | H | L | H | **H** | H | M | H | H | L |
| `019` Hash-chained tamper evidence | H | M | M | M | M | H | M | M | H | H | M | **H** | H | H | H |
| `030` Multi-tenancy | H | H | H | H | H | H | **H** | H | H | H | H | M | H | H | H |
| `031` Streaming channels/telemetry | H | — | **H** | H | M | M | — | H | L–M | L | L | L | L | L–M | L |
| `032` Binary attachments | H | M | M | H | H | L | H | L | M | **H** | M | H | H | H | M |
| `033` Multi-origin replication | H | M | H | M | H | H | L | H | M | M | H | M | M | M | **H** |
| `034` Application-level sharding | H | M | H | M | H | H | L | H | M | M | H | M | M | M | **H** |
| `035` Non-authoritative capture | H | H | H | H | M | M | L | H | H | M | H | L | **H** | M | M |
| `036` DID/UCAN self-attestation | M | **H** | M | L | L | L | M | L | L | M | L | L | M | L | L–M |
| `042` Gated authoritative publish/Live View | H | H | M | H | M | M | L | M | **H** | M | H | L | H | M | M |
| `043` Delegated/"secondary opinion" access | **H** | M | L | M | L | H | M | L | M | H | M | M | H | H | L |
| `045` Read access audit log | H | H | L | M | M | H | M | M | H | H | H | H | H | **H** | H |
| `046` RBAC + row-level security | H | M | M | M | M | H | H | M | M | H | M | **H** | H | H | M |
| `057` GDPR/CCPA erasure | H* | H | L | H | L | L* | **H*** | L | M | H* | M | L | H* | M | L |
| `058` Tenant rate limiting | M | H | M | M | **H** | H | M | M | M | L–M | L | L | M | L | M |
| `060` Outbound webhooks | M | H | H | H | H | H | M | M | H | M | H | L | M | M | **H** |
| `061` Data residency/region-pinning | M | H | M | M | M | H | L–M | M | M | M | H | **H** | M | M | L |
| `065` Local active-scope caching + erasure invalidation | **H** | L | M | M | M | L | L | M | L–M | M | L | M | M | H | M |
| `066` Digital sign-off | H | M | L | M | L–M | H | L | L | H | M | L–M | H | H | **H** | M |
| `068` Bitemporal export/playback | H | M | M | H | M | H | L | M | **H** | L–M | M | M | M | H | L–M |
| `069` Pluggable outbox flush triggers | H | L | M | M | M | L | L | M | L | M | L | **H** | M | M | M |
| `070` Device input integration | H | L | H | H | M | L | L | H | L–M | M | L | L | L | H | **H** |
| `071` PCI-DSS SAD registration boundary | — | M | — | M | L | **H** | L | L | — | — | — | — | L | — | — |
| `072` Bulk ingestion + interchange-format adapters | H | M | M | M | H | M | L | M | H | M | **H** | M | M | M | H |

\* **Real, useful tension, not just a checkbox** — clinical trials (ICH
GCP retention), brokerage (FINRA/SEC/MiFID record-keeping), education
(academic-record retention), biobanking (active-study specimen
retention), and government case management (public-records law) all
have *regulatory retention* requirements pushing against erasure —
building in one of these domains would genuinely stress-test whether
`ADR-057`'s per-field `erasureScope` scoping resolves that tension in
practice, not just asserts it.

**Standouts by ADR, not just table filler:**
- **`ADR-036` (DID/UCAN)**: digital identity/KYC is the only domain
  where this stops being a secondary detail and becomes the central
  mechanism — self-attested claims exchanged via Token Exchange are
  exactly what UCAN delegation was designed for.
- **`ADR-007` (derived events, still deferred)**: industrial IoT is the
  best fit — equipment-sensor-to-maintenance-alert is a textbook
  cross-stream join, with no domain yet exercising this mechanism at
  all.
- **`ADR-045` (read access audit log)** and **`ADR-068` (bitemporal
  playback)**: digital forensics scores at or near the top on both,
  alongside lineage, attachments, delegated access, and digital sign-off
  — unsurprising, since several of this session's later ADRs (`064`,
  `066`–`068`, `070`) were motivated by litigation-review requirements
  before this domain was ever named as a candidate.
- **`ADR-005` (lineage)**: biobanking's specimen-to-derived-sample chain
  is literal, not an analogy — the cleanest fit found across all 15 —
  and it also carries the sharpest version of the erasure-vs-retention
  tension (an irreplaceable specimen mid-study vs. a withdrawn consent).
- **`ADR-061` (data residency)**: ITAR is the first candidate where this
  is the domain's *defining*, legally-mandated requirement, not a
  nice-to-have.
- **`ADR-071` (PCI-SAD boundary)**: the only ADR with real "—" (no
  fit at all) across most of the 15 domains — narrowly scoped to
  payment-card handling, strongest in brokerage specifically.

## Regulatory/compliance framework mapping (all 15 domains)

The axis explicitly requested as distinct from technical fit — which
real law/standard governs each domain, checked against the actual
source rather than assumed. Reformatted as a per-domain list rather
than a wide table — easier to scan as plain text, and lets the
cross-cutting frameworks (ones that apply to many domains at once) get
stated once instead of repeated in every row.

**Cross-cutting — applies broadly, not repeated per domain below:**
- **WCAG 2.1 AA / ADA** — any domain rendering through this framework's
  client for real end users, not just the one domain (government case
  management) it was originally tagged to. Resolved as its own standing
  requirement, `ADR-073` — deliberately not folded into `ADR-039`
  specifically, since accessibility applies no matter which UI
  architecture (`ADR-039`'s MVVM, or a fallback per `docs/comparisons/
  ui-architecture-patterns.md`) actually renders a given screen.
- **GDPR Art. 33/34 (breach notification)** — every domain below that
  already lists GDPR for another reason inherits this too; tracked as
  its own open question (`docs/10-open-questions.md`) since the
  notification *workflow* isn't designed yet, only the audit-log inputs
  it would need (`ADR-045`).
- **SOC 2 / ISO 27001** — a baseline expectation for essentially any
  multi-tenant SaaS deployment of this framework, not unique to the one
  domain (KYC) it happened to be listed under originally.

**Per-domain, domain-specific frameworks:**

- **Clinical trials + device telemetry** — FDA 21 CFR Part 11, ICH-GCP,
  HIPAA, GDPR.
- **Digital identity/KYC** — GDPR, eIDAS, BSA/FinCEN KYC rules, and (a
  gap found this session, tracked as an open question) OFAC sanctions
  screening + BSA Suspicious Activity Report filing.
- **Industrial IoT/predictive maintenance** — ISO 55000 (asset
  management), IEC 62443 (industrial cybersecurity) — lightest
  regulatory load of any candidate.
- **Insurance + telematics** — NAIC model laws (state insurance regs),
  HIPAA (health lines), CCPA.
- **Logistics/chain-of-custody** — C-TPAT/AEO customs security
  programs, country-specific export/trade regs.
- **Brokerage/capital markets** — [SEC Rule
  17a-4](https://www.sec.gov/investment/amendments-electronic-recordkeeping-requirements-broker-dealers),
  FINRA, MiFID II (EU), `ADR-071`'s PCI-DSS boundary if card-funded, and
  **SOX Section 404** (confirmed a non-gap — its ITGCs are already
  satisfied by `ADR-045`/`ADR-019`/`ADR-067`, the same pattern as the
  17a-4 finding).
- **Education/credentials** — FERPA (US), W3C Verifiable Credentials
  (digital diplomas).
- **Utilities/smart metering** — NERC CIP (grid cybersecurity), state
  PUC regulations, CCPA (consumption data).
- **Pharmacovigilance** — FDA 21 CFR 314.80/600.80, EMA EudraVigilance,
  ICH E2B(R3) case-report format.
- **Biobanking** — [Common Rule, 45 CFR
  46](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-A/part-46)
  (§46.116 informed consent, broad-consent provision), GDPR Art. 9
  (special category data), ISO 20387 (biobanking quality management).
- **Public health surveillance/registries** — HIPAA's public-health
  exception, state reportable-disease statutes, WHO International
  Health Regulations.
- **ITAR/export-controlled defense data** — ITAR (22 CFR 120–130), EAR
  (15 CFR 730–774), NIST SP 800-171/CMMC.
- **Government case management** — Privacy Act of 1974 (US federal),
  state public-records law.
- **Digital forensics/evidence custody** — [ISO/IEC
  27037:2012](https://www.iso.org/standard/44381.html) (digital evidence
  identification/collection/acquisition/preservation), US Federal Rules
  of Evidence 901/902 (authentication, incl. self-authenticating
  machine-generated data).
- **DSCSA pharma supply chain** — [DSCSA §582(g)(1), 21 U.S.C.
  §360eee-1(g)(1)](https://uscode.house.gov/view.xhtml?req=granuleid%3AUSC-prelim-title21-section360eee-1)
  — enhanced drug distribution security, effective Nov. 2023.

## Recommendation (superseded by the decision at the top of this doc)

This section is kept as the reasoning that led to the decision, per this
project's additive-history convention — not rewritten to look like the
two-domain pick was obvious from the start.

**Clinical trials + connected medical-device telemetry** was the
single strongest choice for a *one-domain* proving ground — it's the
only candidate with no outright gap (every mechanism scores at least M),
and several ADRs (`ADR-043` especially) were already shaped around this
exact scenario.

**Pairing it with digital identity/KYC** was named as a real option
since no single domain fully proves `ADR-036` — between the two, every
mechanism in the matrix reaches H somewhere, with no domain-specific
rewrite of the core engine required (`ADR-030`'s multi-tenant, domain-
agnostic core is exactly what makes running two proving-ground domains
side-by-side a real, low-cost option rather than a second framework).
This is the option direction received this session picked, for the
coverage reason stated here plus one this comparison didn't originally
raise: reducing the risk of the framework reading as built for one
industry.
