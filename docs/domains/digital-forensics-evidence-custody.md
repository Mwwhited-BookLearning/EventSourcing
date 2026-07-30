[← Domains index](README.md)

# Domain: Digital Forensics / Evidence Custody

**Status: Considered, not chosen** — see
`docs/comparisons/proving-ground-domain.md` for the full comparison.
Clinical trials + device telemetry and digital identity/KYC were chosen
instead.

## Overview

A platform managing digital evidence through acquisition, analysis, and
litigation review — disk images, extracted files, timelines, and
examiner attestations, each requiring an unbroken, provable chain of
custody. Of all fifteen candidates this comparison considered, digital
forensics scores H or H-adjacent on more mechanisms than any other
single domain — lineage, attachments, delegated access, the audit log,
digital sign-off, bitemporal export/playback, and device input
integration all land as load-bearing here. That isn't a coincidence:
several of this session's later ADRs (`ADR-064`, `ADR-066`–`068`,
`ADR-070`) were motivated by litigation-review requirements *before*
this domain was ever named as a candidate. This is less a case of "does
this framework fit forensics" and more "this framework already fits
forensics by construction."

## Governing regulations/standards

| Framework | What it governs here |
|---|---|
| ISO/IEC 27037:2012 | Digital evidence identification, collection, acquisition, and preservation |
| US Federal Rules of Evidence 901/902 | Authentication of evidence, including self-authenticating machine-generated data |

## Applicable ADRs

**Primary fit (the domain's defining characteristics):**
- `ADR-005` — event lineage: a derived forensic artifact (an extracted
  file, a decrypted volume, a timeline entry) traces causally to a
  source acquisition image — a literal DAG, not an analogy.
- `ADR-032` — binary attachments: disk images, extracted files, and
  other evidentiary media are attachment-shaped content, the highest
  scoring fit of any current candidate.
- `ADR-043`/`ADR-044` — delegated, capped, time-boxed access grants:
  the exact shape of granting opposing counsel or a secondary examiner
  time-boxed access to specific evidence.
- `ADR-045` — read access audit log: chain-of-custody *is* an access
  audit log — who touched this evidence, when, and under what
  credential — making this one of the cleanest fits found across every
  candidate.
- `ADR-066` — digital sign-off: examiner attestations and chain-of-
  custody sign-offs at each handoff are exactly this mechanism's target
  case.
- `ADR-068` — bitemporal export/playback: "what did this evidence show
  as of collection time, versus what it shows now" reconstructions are
  a routine litigation-review need, not an edge case.
- `ADR-070` — device input integration: forensic acquisition hardware
  (write-blockers, imaging devices, USB/serial-attached tools) maps
  directly onto this mechanism.
- `ADR-046`/`ADR-043` (RLS) — role-based + row-level access: examiners,
  case attorneys, and opposing counsel each need distinctly scoped
  access to the same evidence.
- `ADR-030` — multi-tenancy: multiple cases/matters/firms sharing the
  same platform, each strictly isolated.
- `ADR-009`/`ADR-050`/`ADR-052` — masking/regulatory classification:
  evidence routinely contains PII/PHI that must be redacted for
  discovery without altering the underlying record.

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-035` — non-authoritative capture: a submitted piece of evidence
  pending forensic validation before being treated as authoritative.
- `ADR-033`/`ADR-034` — replication/sharding: moderate, for multi-site
  labs or distributed case teams.
- `ADR-057` — GDPR/CCPA erasure: moderate; evidence retention
  obligations create a tension similar to clinical trials', though less
  central to the domain's identity.
- `ADR-060` — outbound webhooks: moderate, for case-management-system
  notifications.
- `ADR-007` — derived/materialized events: moderate, for timeline or
  pattern-of-life aggregation across many source artifacts.

**Weak/no fit:**
- `ADR-036` (DID/UCAN self-attestation) — no natural self-sovereign
  identity story; examiner identity is credentialed institutionally, not
  self-attested.
- `ADR-031` (streaming channels) — low-to-moderate at best; occasional
  fit for continuous acquisition logging, not a defining characteristic.
- `ADR-058` (tenant rate limiting) — no strong driver; this isn't a
  high-volume public-facing API domain.

## Special concerns

- **Most of the relevant mechanisms were built for this domain's use
  case before the domain itself was named** — `ADR-064`, `ADR-066`
  through `ADR-068`, and `ADR-070` were all motivated by litigation-
  review requirements during this session, well before digital forensics
  was considered as a candidate. The question this domain answers isn't
  "does this framework fit forensics" so much as "this framework already
  fits forensics by construction" — a rare case where the proving-ground
  review confirmed fit after the fact rather than discovering a gap.
- **FRE 901/902 self-authentication maps directly onto existing
  primitives** — the rule's allowance for self-authenticating
  machine-generated data lines up with `ADR-019`'s hash-chained log
  combined with `ADR-066`'s digital sign-off, rather than requiring any
  new authentication mechanism.
- **ISO/IEC 27037's chain-of-custody requirement is this framework's
  read access audit log, not a bespoke addition** — `ADR-045`'s
  `AccessLogEntry`, hash-chained independently of the event log itself,
  is a direct structural match for what evidence-handling standards
  already require documented.
- **Delegated access here is adversarial in a way clinical trials'
  "secondary opinion" access isn't** — granting opposing counsel access
  to evidence is a genuinely different trust posture than a clinician
  seeking a colleague's opinion; both use `ADR-043`'s same capped,
  entity-scoped grant mechanism, but the review should note the
  difference in intent rather than assume the identical mechanism means
  an identical use case.
- **Accessibility (`ADR-073`)** — examiner- and case-attorney-facing
  review screens (including `ADR-068`'s litigation-review playback UI)
  render through this framework's client the same as any other domain;
  WCAG 2.1 AA applies here too, not just the government-case-management
  candidate it was originally tagged under.

## Glossary

- **Bit-for-Bit (Forensic) Image** *(synonym: forensic image, disk image — used interchangeably for the same sector-by-sector copy)* — an exact, sector-by-sector copy of
  a storage device, including unallocated space and deleted-file
  remnants, made so all analysis happens on a copy and the original
  media is never touched — the attachment-shaped content `ADR-032`
  scores highest against of any candidate reviewed.
- **Chain of Custody** *(synonym: chain of evidence)* — the unbroken, documented record of who
  possessed, accessed, or transferred a piece of evidence and when,
  from collection through court presentation; a single undocumented gap
  can render evidence inadmissible — restated in Special concerns above
  as, structurally, `ADR-045`'s read access audit log.
- **Digital Fingerprint / Hash Value** — a fixed-length checksum (e.g.,
  SHA-256) computed from a file or image's contents, which changes if
  even a single bit is altered, used to prove a copy is identical to
  its source; paired with `ADR-066`'s digital sign-off, this is the
  combination Special concerns above ties to FRE 901/902
  self-authentication.
- **E-Discovery** *(synonym: electronic discovery — "e-" is literally short for "electronic," not an unrelated abbreviation)* — the process of identifying, preserving, collecting,
  and producing electronically stored information in response to
  litigation or a regulatory investigation — the routine litigation-
  review need `ADR-068`'s bitemporal export/playback targets directly.
- **Examiner** *(synonym: forensic analyst — CISA's own Cyber Defense Forensics Analyst work role lists "Digital Forensic Examiner" as an alternate title for the same role)* — the credentialed forensic professional who performs
  acquisition, analysis, and interpretation of digital evidence and
  attests to the findings — the role `ADR-066`'s digital sign-off
  captures at each chain-of-custody handoff.
- **FRE 901 (Federal Rule of Evidence 901)** — the rule requiring a
  party to produce evidence sufficient for a reasonable factfinder to
  conclude an item is what it's claimed to be, before it can be
  admitted at trial.
- **FRE 902 / Self-Authenticating Evidence** — categories of evidence,
  including certified electronically generated records under Rules
  902(11)-(14), that require no extrinsic proof of authenticity to be
  admitted — the rule Special concerns above says lines up directly
  with the hash chain and `ADR-066`'s sign-off, with no new mechanism
  needed.
- **ISO/IEC 27037** — the international standard giving guidance to
  Digital Evidence First Responders and Digital Evidence Specialists on
  identifying, collecting, acquiring, and preserving digital evidence
  so its integrity and cross-jurisdiction admissibility are preserved;
  Special concerns above calls `ADR-045`'s access audit log a direct
  structural match for its chain-of-custody requirement.
- **Legal Hold** *(synonym: litigation hold — used near-interchangeably in practice, though litigation hold is technically the narrower civil-litigation subset of the broader legal-hold preservation duty)* — a notice issued once litigation is reasonably
  anticipated, obligating an organization to preserve all potentially
  relevant records and suspend routine deletion — creating the same
  retention-vs-erasure tension `ADR-057` is named against as a real but
  secondary concern here.
- **Metadata Preservation** — retaining a file's system-level
  attributes (timestamps, paths, ownership) exactly as found during
  acquisition, since altering metadata can itself constitute evidence
  tampering.
- **Pattern-of-Life Analysis** — reconstructing a subject's routine
  behavior — movements, communications, device usage — by correlating
  many discrete digital artifacts over time; the timeline aggregation
  `ADR-007`'s derived/materialized events are named against above.
- **Spoliation** — the intentional or negligent destruction,
  alteration, or loss of evidence, which can trigger court sanctions or
  an adverse-inference instruction against the responsible party; an
  immutable `ADR-045` access log makes undetected spoliation of the
  record itself detectable.
- **Write Blocker** *(synonym: forensic bridge — the term hardware vendors like Tableau and WiebeTech use for the same device)* — hardware or software that lets an examiner read a
  storage device with no possibility of writing to it, preventing
  accidental modification of original evidence during acquisition — one
  of the forensic acquisition devices `ADR-070`'s device input
  integration is named against directly.
