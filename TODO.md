# TODO

A live tracker for **concrete, already-decided work that just hasn't
been done yet** — distinct from both other live trackers in this repo:

- [`docs/10-open-questions.md`](docs/10-open-questions.md) is for a
  design fork **not yet decided** — the question itself is still open.
- **This file** is for a task where the decision is already made (a doc
  needs rewriting, a diagram needs drawing, a terminology collision
  needs resolving) and only the doing is left.
- [`docs/changes/{date}.md`](docs/changes) is the narrative history of
  work **already completed** — where an item here goes once it's done.

**Full workflow (adding/completing items, batching large ones) is in
[`.claude/protocols/todo-tracking.md`](.claude/protocols/todo-tracking.md)
— read it before touching this file.** Short version: add an item the
same pass you find one; when it's done, delete the item here and add a
line to today's `docs/changes/{date}.md` instead.

**This is the authoritative list of active work** — per the same
reasoning `docs/10-open-questions.md` already applies to itself, do not
restate this list's contents elsewhere in the repo (including in
`CLAUDE.md`); a duplicated copy just drifts stale. `CLAUDE.md` points
here instead of inlining.

## Active

- [ ] **Add real `x-masking-searchable` examples to Vitals' `Patient
  Enrollment and Informed Consent`** (`docs/domains/clinical-trials-
  device-telemetry/features/patient-enrollment-and-informed-consent.md`):
  `LegalName` (`Equality`, `High` cardinality, `Shared` scope) and
  `DateOfBirth` (`Equality`, `Low` cardinality, `Shared` scope — needs
  the guardrail fix above landed first, or an explicit
  `acknowledgeLeakageRisk` in the example). Document the **compound-
  match** mitigation explicitly: a real duplicate-subject-detection or
  subject-rights-intake query should require both `LegalName` AND
  `DateOfBirth` to match, never `DateOfBirth` alone — meaningfully
  raises the bar against frequency analysis on the low-cardinality half.
  Cross-reference from `trial-data-export-and-subject-rights.md`, where
  the "caller identifies themselves by name/DOB, not their internal
  `SubjectId`" workflow actually lives.
- [ ] **Add real `x-masking-searchable` examples to Meridian's
  `Customer Onboarding and Identity Verification`** (`docs/domains/
  digital-identity-kyc/features/customer-onboarding-and-identity-
  verification.md`): `DateOfBirth` (`Range`, `Low` cardinality — the
  canonical guardrail example, a real age-eligibility query) and
  `ClaimedLegalName` (`Equality`, `High` cardinality — duplicate-
  applicant detection across DIDs).
- [ ] **Add real `x-masking-searchable` examples to Meridian's remaining
  two feature docs**: `document-and-biometric-capture.md`'s
  `ExtractedDocumentNumber` (`Equality`, `High` cardinality — real
  fraud-detection need: has this exact document number already onboarded
  a *different* applicant); `periodic-screening-and-sar-escalation.md`'s
  `MatchedListEntryId` (`Equality`, likely `Low` cardinality — a bounded,
  enumerable sanctions-list domain, a second real instance of the
  guardrail-fix item above — a compliance officer searching "every
  current applicant flagged against this specific sanctions entry").
- [ ] **Build automated Playwright UI playbooks with screenshots,
  assembled into markdown user guides — direct request, confirmed this
  session that nothing like this exists yet.** `ADR-055` already decided
  Playwright (.NET, MSTest base classes) for UI action tests and named a
  `EventStore.E2ETests` project — never actually built; the one real
  Playwright run this project has done was a single throwaway Docker
  container used once for an ad-hoc visual spot-check (`08-build-plan.md`,
  "Proving-Ground Application UX" item), with no screenshots committed
  anywhere and no markdown produced. This is a **new mechanism**, not
  just "finally build the named project" — needs its own small design
  pass before code:
  - Finally stand up `tests/EventStore.E2ETests` per `ADR-055`'s own
    already-decided shape.
  - Each test walks one real user workflow step-by-step (matching a
    domain's `Workflow` + feature doc — Vitals' Workflows A–D, Meridian's
    A–C — or a core-engine `docs/features/*.md` doc for non-domain-
    specific UI), capturing a screenshot at each meaningful step via
    Playwright's own screenshot API.
  - Screenshots get assembled into a markdown playbook, **named for the
    epic and feature** per direct request — this project's closest
    existing "epic" concept is a domain's own `Workflow` letter (e.g.
    Vitals Workflow A), so the natural mapping is `{Workflow}-{Feature
    doc name}.md`, e.g. `docs/playbooks/vitals/workflow-a-patient-
    enrollment-and-informed-consent.md` — needs confirming with the user
    rather than assumed, since "epic" isn't literally this project's own
    term anywhere yet.
  - Needs a small new pattern doc (`docs/patterns/`) or ADR describing
    the actual mechanism (screenshot-capture-to-markdown-assembly
    pipeline, where playbooks/screenshot assets physically live, the
    naming convention once confirmed) — per this project's own "search
    for prior art / write the decision down before building a new
    mechanism" standing convention, not skipped just because the ask is
    small in scope.
