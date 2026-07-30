[← Domains index](README.md)

# Domain: Government Case Management

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A casework platform for public-sector programs — social services,
benefits eligibility, licensing/permitting, or investigative case
files — where a case record accumulates submissions, determinations,
and inter-agency referrals over its lifecycle. It scores consistently
high (H) across most of the current mechanism list: non-authoritative
capture (citizen- or field-worker-submitted data pending caseworker
review), binary attachments (supporting documents, forms, photographs),
multi-tenancy (multiple agencies/programs), RBAC/row-level security
(strict need-to-know across caseworkers, supervisors, and external
auditors), masking, delegated access (inter-agency referrals), the read
access audit log, and digital sign-off (eligibility determinations,
approvals). Its one real tension is the same retention-vs-erasure shape
already named for clinical trials and brokerage: public-records law
requires retention that erasure requests would otherwise conflict with.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| Privacy Act of 1974 (US federal) | Federal system-of-records handling, individual access/amendment rights |
| State public-records law | Retention and public-disclosure obligations for government case files |
| Section 508 accessibility | Accessibility requirements for government-facing systems and their UIs |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-035` — non-authoritative capture: citizen- or field-worker-
  submitted case information is captured immediately but isn't
  authoritative until a caseworker reviews and accepts it.
- `ADR-032` — binary attachments: supporting documentation, forms,
  photographs, and correspondence attached to a case record.
- `ADR-030` — multi-tenancy: multiple agencies and programs sharing the
  same platform, each with its own case types and rules.
- `ADR-046`/`ADR-043` — RBAC + row-level security: strict need-to-know
  access across caseworkers, supervisors, and external auditors, often
  by program or case sensitivity.
- `ADR-009`/`ADR-050`/`ADR-052` — masking/regulatory classification:
  case records routinely carry PII and sensitive personal circumstances.
- `ADR-043` — delegated access: inter-agency referrals and secondary
  reviewers needing capped, time-boxed access to a case.
- `ADR-045` — read access audit log: public accountability for who
  accessed a citizen's case record and when.
- `ADR-066` — digital sign-off: eligibility determinations, benefit
  approvals, and other attestable case decisions.
- `ADR-057` — GDPR/CCPA erasure: real, but in tension with public-
  records retention law — see Special concerns.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-005` — event lineage: a determination or referral derives
  causally from prior submissions and reviews, a real but modest DAG.
- `ADR-036` — DID/UCAN self-attestation: plausible for citizen identity
  verification at case intake, not central.
- `ADR-033`/`ADR-034` — replication/sharding: moderate, mainly for
  multi-office/multi-jurisdiction deployments.
- `ADR-060` — outbound webhooks: notifying partner agencies of case
  status changes.
- `ADR-058` — tenant rate limiting: moderate, for high-volume public-
  facing intake APIs.
- `ADR-068` — bitemporal export/playback: moderate, useful for
  after-the-fact audits or appeals review.
- `ADR-007` — derived/materialized events: low-to-moderate, for
  aggregate program reporting.

## Special concerns

- **Retention vs. erasure, the same real tension named for clinical
  trials and brokerage**: state public-records law can require long
  retention of case files, in tension with any erasure right a citizen
  might otherwise invoke. `ADR-057`'s `erasureScope`-driven, per-field
  crypto-shredding (erase identifying data, keep the record structurally
  intact) would be genuinely stress-tested here, not just asserted to
  work.
- **Section 508 accessibility is a UI-layer concern this framework
  doesn't itself resolve** — it constrains whatever client is built
  against `ADR-039`'s MVVM architecture, not the event-sourcing core;
  worth naming so it isn't mistaken for a gap in the data/API layers.
  Concretely satisfied via `ADR-073`'s WCAG 2.1 AA baseline — this
  domain is where accessibility was originally (too narrowly) tagged
  before that ADR generalized it as cross-cutting to every domain's
  client, not unique to government case management.
- **Need-to-know granularity can be unusually fine-grained** — case
  types touching especially sensitive populations (e.g., child welfare,
  benefits-fraud investigations) may need row-level restrictions finer
  than "caseworker vs. supervisor," a real test of `ADR-043`'s
  entity-scoped claim generalization beyond its original delegated-grant
  framing.
- **No natural streaming-telemetry or device-input story** — unlike
  digital forensics or DSCSA, government case management has no
  organic fit for `ADR-031`/`ADR-070`; case data arrives as documents
  and structured submissions, not sensor or device readings.
