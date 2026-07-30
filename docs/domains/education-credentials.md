[← Domains index](README.md)

# Domain: Education / Credentials

**Status: Considered, not chosen** — see `docs/comparisons/proving-ground-domain.md` for the full comparison. Clinical trials + device telemetry and digital identity/KYC were chosen instead.

## Overview

An education/credentialing platform — student academic records,
transcripts, and digital diplomas/certificates issued by one or more
institutions. Reviewed as one of the original eight proving-ground
candidates; strongest on the mechanisms a records-and-documents domain
naturally exercises (binary attachments, multi-tenancy, RBAC/row-level
security, masking/regulatory classification, and a genuine
retention-vs-erasure tension), but the lightest-touch candidate for most
of the framework's more specialized or investigative mechanisms — no
natural event-lineage, non-authoritative-capture, streaming, or forensic
story.

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| FERPA (US) | Protection of student education records |
| GDPR | EU student/subject data, right to erasure |
| W3C Verifiable Credentials | Digital diplomas/certificates as cryptographically verifiable credentials |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-032` — binary attachments: diplomas, transcripts, certificates,
  and supporting documents are this domain's core content.
- `ADR-030` — multi-tenancy: multiple institutions each issuing/managing
  their own records.
- `ADR-046`/`ADR-043` (RLS) — role-based + row-level access: registrar,
  instructor, student, and employer/relying-party each need different
  access to the same record.
- `ADR-009`/`ADR-050`/`ADR-052` — masking + regulatory classification:
  FERPA-classified education-record fields.
- `ADR-057` — GDPR/CCPA erasure: scores H*, footnoted alongside clinical
  trials and brokerage — academic-record retention requirements directly
  test the erasure-vs-retention tension.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-036` — DID/UCAN self-attestation: W3C Verifiable Credentials for
  digital diplomas is a real use, scoring M — but this domain didn't
  drive `ADR-036`'s design the way digital identity/KYC's central
  self-attestation use case did.
- `ADR-043` — delegated/"secondary opinion" access: moderate — e.g. a
  relying-party employer granted temporary, scoped access to verify a
  credential.
- `ADR-045` — read access audit log: moderate — who looked up a
  student's record, and when.
- `ADR-060` — outbound webhooks: moderate — notifying a relying party of
  a credential-status change.
- `ADR-058` — tenant rate limiting: moderate — many relying
  parties/verifiers querying the same institution's records.
- `ADR-061` — data residency/region-pinning: borderline (scores L–M in
  the matrix) — plausible for cross-border institutions, but not a
  strong driver.

**Weak/no fit:**
- `ADR-031` — streaming channels: scores **—**, no realistic fit — the
  same weak spot digital identity/KYC has, no natural telemetry story
  for an academic-records domain.
- Several other mechanisms score L across the board rather than having
  any individually notable story: event lineage/DAG (`ADR-005`),
  non-authoritative capture (`ADR-035`), replication/sharding
  (`ADR-033`/`ADR-034`), derived/materialized events (`ADR-007`),
  digital sign-off (`ADR-066`), bitemporal export/playback (`ADR-068`),
  and device input integration (`ADR-070`) — technically applicable, but
  this domain has no natural DAG-shaped derivation, no
  capture-pending-review workflow, and no device/investigative story to
  make any of them load-bearing.

## Special concerns

- **Retention vs. erasure tension** — footnoted in the comparison
  alongside clinical trials and brokerage: academic-record retention
  requirements (institutional policy, accreditation, transcript-
  authenticity needs) push against GDPR/CCPA erasure and a student's own
  erasure rights. Building here would stress-test `ADR-057`'s
  `erasureScope`-driven, per-field crypto-shredding the same way
  clinical trials does.
- **Weak spots**: no natural streaming-telemetry story at all (mirroring
  digital identity/KYC's own weak spot), and DID/UCAN self-attestation
  only reaches M here — a real use for digital-diploma verifiable
  credentials, but secondary, not the central mechanism it is for
  digital identity/KYC.
- **Lightest regulatory/technical load of the strongest candidates
  reviewed** — no strong lineage/DAG story, no non-authoritative-capture
  workflow, no replication/sharding driver, and none of the newer
  forensic-shaped mechanisms (digital sign-off, bitemporal playback,
  device input integration) find a natural home here.
- **Accessibility (`ADR-073`)** — student/registrar/employer-facing
  screens render through this framework's client the same as any other
  domain; WCAG 2.1 AA applies here too, not just the
  government-case-management candidate it was originally tagged under.
- **GDPR breach notification (Art. 33/34)** — this domain already relies
  on GDPR for the erasure-vs-retention tension above; the 72-hour
  notification *workflow* itself remains an open question
  (`docs/10-open-questions.md`) — `ADR-045`'s access audit log supplies
  the forensic inputs, but the notification process itself isn't
  designed yet.

## Glossary

- **Accreditation** — the voluntary, third-party quality-assurance
  process (regional or national accreditors in the US) that certifies an
  institution or program meets recognized academic standards;
  accredited status is usually a prerequisite for federal financial aid
  and for other institutions/employers recognizing a credential.
- **Credential Holder** — the individual (student or graduate) a
  transcript, diploma, or verifiable credential is actually about — the
  "subject" in W3C Verifiable Credentials terminology.
- **Credential Issuer** — the institution (or, in W3C Verifiable
  Credentials terms, the "issuer") that creates and digitally signs a
  credential — the role `ADR-036`'s DID/UCAN self-attestation would
  assign to an accredited institution issuing a diploma.
- **Credential Verifier (Relying Party)** — a third party — an employer,
  another institution, a licensing board — that checks a presented
  credential's validity and issuer, typically without needing to
  contact the issuer directly; the scenario `ADR-043`'s delegated,
  temporary access grant is scored against.
- **Digital Diploma** — a diploma or certificate issued as a
  cryptographically verifiable digital artifact rather than (or
  alongside) a paper original — this domain's clearest use case for
  `ADR-032`'s binary attachments.
- **Directory Information** — the narrow category of FERPA-covered data
  (name, dates of attendance, degrees/honors, and similar) a school may
  disclose without consent, having given public notice and an opt-out
  window — the boundary FERPA-classified fields (`ADR-009`/`ADR-050`/
  `ADR-052`) would need to encode correctly, since most other
  education-record fields require consent by default.
- **Eligible Student** — FERPA's term for a student who has either
  turned 18 or is attending a postsecondary institution, at which point
  FERPA's rights transfer from the parent to the student directly.
- **FERPA (Family Educational Rights and Privacy Act)** — the US federal
  law protecting the privacy of student education records, giving
  parents (or eligible students) the right to inspect, seek correction
  of, and control disclosure of those records.
- **Micro-credential / Digital Badge** — a narrower, often single-skill
  credential (short of a full degree) issued for completing a specific
  course or demonstrating a specific competency — a smaller-grained
  analog of the digital diploma, raising the same binary-attachment and
  verifiable-credential questions at finer granularity.
- **Registrar** — the institutional office of record responsible for
  maintaining official academic records, enrollment, and transcripts —
  the role this domain's row-level-access scenarios (`ADR-046`/
  `ADR-043`) most often grant the broadest access to.
- **Transcript** — the institution's official, cumulative record of a
  student's coursework, grades, and credits earned — this domain's
  other core `ADR-032` binary-attachment use case alongside the diploma
  itself.
- **Transfer Credit** — academic credit earned at one institution and
  accepted toward a credential at another, requiring the receiving
  institution to trust the sending institution's transcript — a
  real-world instance of the credential-verification trust question
  `ADR-036` addresses generally.
- **Verifiable Credential (W3C VC)** — the W3C's standardized,
  cryptographically secured, machine-readable credential format
  (issuer/claims/proof) — the concrete standard behind this domain's
  `ADR-036` fit for digital diplomas, scored as a real but secondary use
  of that mechanism.
