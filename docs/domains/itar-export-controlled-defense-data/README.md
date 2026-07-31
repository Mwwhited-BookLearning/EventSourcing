[← Domains index](../README.md)

# Domain: ITAR/Export-Controlled Defense Data

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A platform holding export-controlled defense technical data — drawings,
specifications, source code, and related artifacts subject to the
International Traffic in Arms Regulations and the Export Administration
Regulations. Across all fifteen domains this comparison considered, ITAR
is the first one where `ADR-061`'s data-residency/region-pinning
mechanism is the domain's *defining* requirement rather than a
nice-to-have: ITAR data must stay restricted to US persons/US soil by
law, not by policy choice. It also scores strongly on row-level access
control, digital sign-off, and the audit log — access to controlled
technical data is itself a regulated act, not just its disclosure.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| ITAR (22 CFR 120–130) | Export-controlled defense articles/technical data — who may access it and where |
| EAR (15 CFR 730–774) | Dual-use export controls, the civilian-adjacent counterpart to ITAR |
| NIST SP 800-171 / CMMC | Controlled Unclassified Information handling requirements for defense contractors |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-061` — data residency/region-pinning: **the domain's defining
  requirement**, not a nice-to-have — ITAR data must stay restricted to
  US persons/US soil by statute, and this is the first candidate across
  all fifteen reviewed where that's true.
- `ADR-046`/`ADR-043` — RBAC + row-level security: access to controlled
  technical data is itself a regulated act (who may even *view* a
  drawing matters as much as who may edit it), not just a convenience.
- `ADR-066` — digital sign-off: export-control release approvals and
  technical-data-access authorizations are exactly the kind of
  attestable, step-up-authenticated action this mechanism targets.
- `ADR-045` — read access audit log: CMMC/NIST SP 800-171 access
  accountability requirements map directly onto this mechanism.
- `ADR-032` — binary attachments: the technical data itself (drawings,
  specifications, source packages) is largely attachment-shaped content.
- `ADR-005` — event lineage: a derived or redacted technical-data
  artifact traces causally to its controlled source, a real DAG.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-030` — multi-tenancy: multiple programs/contracts/contractors
  sharing the same platform.
- `ADR-009`/`ADR-050`/`ADR-052` — masking/classification: applicable to
  metadata fields, secondary to the access-control story above.
- `ADR-043` — delegated access: controlled sharing with a cleared
  subcontractor or foreign-partner exception, time-boxed and capped.
- `ADR-033`/`ADR-034` — replication/sharding: moderate, mainly in
  service of region-pinning rather than scale.
- `ADR-007` — derived/materialized events: moderate, for
  export-control-relevant aggregate reporting.
- `ADR-068` — bitemporal export/playback: moderate, useful for
  after-the-fact export-control compliance review.
- `ADR-074` — SBOM/SOUP list: NIST SP 800-171/CMMC's controlled-
  unclassified-information handling requirements include software
  supply-chain security, a real driver for `ADR-074`'s SBOM generation
  alongside clinical trials' direct FDA Section 524B requirement.

**Weak/no fit:**
- `ADR-035` (non-authoritative capture) — controlled technical data
  entering the system is typically already vetted through an
  export-control review process before ingestion, not organically
  "pending" the way a clinical reading or a self-attested identity claim
  is.
- `ADR-036` (DID/UCAN self-attestation) — no natural self-sovereign
  identity story; access is governed by clearance/authorization records,
  not self-attested claims.
- `ADR-031` (streaming channels) — no natural telemetry story.
- `ADR-057` (GDPR/CCPA erasure) — export-controlled records are governed
  by federal recordkeeping/retention obligations, not a personal-data
  erasure right; there is no natural pull toward this mechanism at all,
  unlike clinical trials' or brokerage's genuine retention-vs-erasure
  tension.

## Special concerns

- **Region-pinning is not optional here** — unlike digital identity/KYC
  or public health surveillance, where `ADR-061` is a strong but still
  secondary driver, ITAR's US-persons/US-soil restriction is a hard
  legal requirement of the domain itself. A build against this domain
  would need `ADR-061` enforced at the core, not bolted on afterward.
- **Access control is the compliance surface, not just disclosure** —
  ITAR/EAR violations can occur simply from an unauthorized person
  *viewing* controlled technical data, which is why RBAC/row-level
  security and the read access audit log both score high here: the
  audit log isn't just forensic record-keeping, it's the primary
  evidence of compliance.
- **No natural erasure story** — this domain has essentially the inverse
  shape of digital identity/KYC's "erasure is routine" note: retention
  and controlled access dominate, and there is no realistic erasure
  driver to stress-test `ADR-057` against.
- **Foreign-person exceptions are a delegated-access shape, not a
  bespoke mechanism** — a licensed technical-assistance agreement
  permitting a specific foreign national time-boxed access to specific
  data is a real-world instance of `ADR-043`'s capped, entity-scoped
  grant, not a new access-control primitive this domain would require.
- **Accessibility (`ADR-073`)** — cleared-personnel-facing screens
  render through this framework's client the same as any other domain;
  WCAG 2.1 AA applies here too, though the driver is weaker than for a
  citizen-facing domain since the user population is small and
  internally cleared rather than the general public.

## Feature docs

- [`features/controlled-technical-data-access-request.md`](features/controlled-technical-data-access-request.md) — publishing a controlled technical-data asset under an ITAR-scoped `AppId` with region-pinned replication (`ADR-061`), and reading it via ordinary RBAC (`ADR-046`) or a TAA-scoped delegated grant (`ADR-043`, `ADR-066`, `ADR-045`).

## Glossary

- **CCL (Commerce Control List)** — the EAR's master list of dual-use
  items subject to export licensing, organized into ten categories
  (0–9); the list an ECCN is assigned from, distinct from ITAR's
  separate US Munitions List.
- **CMMC (Cybersecurity Maturity Model Certification)** — the Department
  of Defense's tiered cybersecurity certification program for
  contractors handling Federal Contract Information and CUI, with Level
  2 requiring alignment to NIST SP 800-171 — already named in this
  file's regulations table as a driver for `ADR-074`'s SBOM generation
  and `ADR-045`'s access accountability.
- **Controlled Unclassified Information (CUI)** — unclassified
  government-related information that law, regulation, or
  government-wide policy nonetheless requires be safeguarded or have its
  dissemination controlled — the category NIST SP 800-171/CMMC (this
  file's regulations table) governs, distinct from and less restrictive
  than classified information (which this domain doc does not address).
- **DDTC (Directorate of Defense Trade Controls)** — the office within
  the US State Department's Bureau of Political-Military Affairs that
  administers ITAR and the US Munitions List, including licensing
  defense-article exports and Technical Assistance Agreements.
- **Deemed Export** — under the EAR, releasing controlled technology or
  source code to a foreign national anywhere, including inside the US,
  which the regulation treats as an export to that person's home country
  even though nothing physically crosses a border; ITAR reaches the same
  situation without using this specific term. A direct real-world
  instance of the access-vs-disclosure distinction this file's Special
  concerns section makes: viewing controlled data is itself the
  regulated act, not just its transmission abroad.
- **Defense Article** — an item (including technical data) enumerated on
  the US Munitions List and therefore subject to ITAR export controls —
  the category of content `ADR-032`'s binary attachments largely
  represent for this domain, per this file's Applicable ADRs section.
- **EAR (Export Administration Regulations, 15 CFR 730–774)** — the
  Commerce Department's export-control regime for dual-use items
  (civilian applications with potential military relevance), the
  counterpart to ITAR named in this file's regulations table; a given
  item falls under one regime or the other, never both.
- **ECCN (Export Control Classification Number)** — the alphanumeric
  code (e.g., `4A001`) the Commerce Control List uses to classify a
  dual-use item and determine its EAR licensing requirements; an item
  outside the CCL is classified `EAR99`, subject to lighter control.
- **Foreign Person** — under ITAR, anyone who is not a US citizen,
  lawful permanent resident, or one of the other protected categories
  composing `US Person` below; the counterpart category whose access to
  controlled technical data (including via a deemed export) this
  domain's core workflow exists to restrict.
- **ITAR (International Traffic in Arms Regulations, 22 CFR 120–130)** —
  the State Department's export-control regime for defense articles,
  services, and related technical data, named as this domain's defining
  framework in its regulations table; administered by `DDTC`, the office
  `ADR-061`'s region-pinning ultimately exists to satisfy here.
- **Technical Assistance Agreement (TAA)** — a DDTC-approved written
  agreement authorizing a US company to disclose controlled technical
  data or perform defense services for a specific foreign party — the
  real-world instrument behind this file's Special concerns note that a
  foreign-person exception is "a delegated-access shape, not a bespoke
  mechanism," i.e., `ADR-043`'s capped, entity-scoped grant.
- **Technical Data** *(synonym: "technology" — the EAR's parallel
  defined term for the same underlying concept of controlled
  technical/design information; the two agencies simply use different
  names for it, consistent with this file's EAR entry noting an item
  falls under one regime or the other, never both)* — under ITAR (22
  CFR 120.10), information required
  for the design, development, production, operation, or maintenance of
  a defense article — drawings, specifications, and documentation — the
  actual content this domain doc's Overview names as what the platform
  holds, and the attachment-shaped content `ADR-032` covers.
- **US Munitions List (USML)** — ITAR's catalog of controlled defense
  articles, services, and technical data, organized into 21 categories
  from firearms (Category I) to a catch-all (Category XXI); the list
  that makes an item a `Defense Article` subject to ITAR rather than
  EAR.
- **US Person** — under ITAR (22 CFR 120.15), a US citizen, lawful
  permanent resident, protected individual, or an entity incorporated
  and doing business in the US; the population this domain's `ADR-061`
  region/access restriction exists to distinguish from a
  `Foreign Person`.
