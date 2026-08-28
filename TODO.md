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

- [ ] **Fix `ADR-096`'s registration guardrail: extend the cardinality
  check to `Equality`-kind fields, not just `Range`.** Found while
  reviewing the two proving-ground domains for real
  `x-masking-searchable` candidates (Vitals' `DateOfBirth`, Meridian's
  `MatchedListEntryId` — both low-cardinality, both wanted as
  `Equality`, neither currently gated). The paper the guardrail already
  cites ([Naveed/Kamara/Wright, CCS 2015](https://www.microsoft.com/en-us/research/publication/inference-attacks-property-preserving-encrypted-databases/))
  names **deterministic encryption** — exactly what an `Equality` blind
  index is, functionally — as vulnerable to frequency analysis, not
  only order-preserving encryption; the current guardrail only checks
  `if (indexKind == "Range")`, so a `Low`-cardinality classified field
  can register `Equality` with no warning at all. Fix:
  `src/EventStore.SchemaRegistry/MaskingSchemaValidator.cs`'s
  `ValidateSearchableConfig` — require `cardinality` and the
  `acknowledgeLeakageRisk`-gated check for `Equality` too, not only
  `Range`. Update in the same pass: `docs/adrs/adr-096-searchable-blind-
  index-bucketed-range.md`'s Decision text (currently states the
  guardrail as Range-only), `docs/data/schema-registry.md`'s
  `SearchableIndexConfig` description, and add a regression test
  alongside the existing ones in
  `tests/EventStore.IntegrationTests/SearchableEncryptionSqliteTests.cs`.
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
