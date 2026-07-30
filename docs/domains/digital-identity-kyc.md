[← Domains index](README.md)

# Domain: Digital Identity / KYC

**Status: Chosen proving-ground domain** (one of two — see
`docs/comparisons/proving-ground-domain.md` for the full comparison and
decision reasoning).

## Overview

An identity-verification/relying-party-onboarding platform (KYC —
Know Your Customer). Chosen specifically because it's the one domain
that makes `ADR-036`'s DID/UCAN adoption *central* rather than
secondary — self-attested identity claims, exchanged via Token Exchange
for a verifiable credential, are exactly what UCAN delegation was
designed for. Paired with clinical trials rather than built alone, both
for combined feature coverage and to avoid this framework reading as
built for one industry.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| GDPR | EU subject data, right to erasure |
| eIDAS | EU cross-border electronic identification |
| BSA/FinCEN KYC rules | US anti-money-laundering identity-verification requirements |
| SOC 2 | Relying-party trust/security expectations for an identity-verification service |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-036` — DID/UCAN self-attestation, exchanged via OAuth Token
  Exchange (RFC 8693) — the domain's central mechanism, not incidental.
- `ADR-035` — non-authoritative capture: a self-attested identity claim
  is captured immediately, verified/adjudicated later.
- `ADR-057` — GDPR erasure via crypto-shredding — real subject
  erasure requests are a routine KYC-platform occurrence.
- `ADR-060` — outbound webhooks: notifying relying parties of
  verification status changes.
- `ADR-058` — tenant rate limiting: many relying parties/API consumers
  calling the same verification service.
- `ADR-045` — read access audit log — compliance-driven access
  accountability.
- `ADR-047` — claims augmentation for federated IdPs, when a relying
  party's own IdP needs enrichment with this platform's
  verification-specific claims.

**Secondary fit:**
- `ADR-009`/`ADR-050`/`ADR-052` — PII masking/classification.
- `ADR-030` — multi-tenancy (multiple relying parties).
- `ADR-032` — binary attachments (ID document scans/photos).
- `ADR-033`/`ADR-034` — replication/sharding, moderate.
- `ADR-061` — data residency — many countries require identity data to
  stay in-country, a real driver for this mechanism.
- `ADR-043`/`ADR-044` — delegated access/application-defined
  permissions, moderate.

**Weak/no fit:**
- `ADR-031` (streaming channels) — no natural telemetry story at all,
  the mirror image of clinical trials' weak spot (DID/UCAN).

## Special concerns

- **No natural streaming-telemetry use** — if this domain is ever
  extended toward continuous biometric verification (e.g., liveness
  video), `ADR-031`/`ADR-070` become relevant; not needed for the
  baseline KYC verification workflow.
- **Erasure is routine here, not exceptional** — unlike clinical
  trials/brokerage, this domain has no strong retention-vs-erasure
  tension pulling the other way; a KYC platform should expect and
  handle erasure requests as ordinary traffic.
- **Data residency is a first-order concern, not an edge case**
  (`ADR-061`) — many jurisdictions legally require identity-verification
  data to stay within-country; this domain is a strong real-world
  driver for that mechanism, not a hypothetical one.
