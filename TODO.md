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

- [ ] **Meridian's Workflow B (Relying-Party Access) has no `client-web`
  UI surface at all — building one is a real new UI feature, not
  wiring.** UI-playbook coverage now spans 7 of the two domains'
  workflows (`docs/playbooks/README.md`'s catalog) — every remaining
  gap this session found either has no UI to walk through
  (this item) or needs an authentication flow the seeder can't perform
  (the next item). Confirmed the mechanism itself is real and already
  proven — `MeridianWorkflowBHttpSqliteTests.cs` exercises a full
  UcanDelegation + OAuth Token Exchange + `revealField` round trip
  end to end — but it's a delegation token used for a GraphQL
  mutation, never a `StoredEvent`/browsable entity (confirmed in that
  test file's own header comment). `client-web`'s only screens are
  generic entity Browse/Detail and event Compose; there is no delegation-
  request or field-reveal screen anywhere in `client-web/packages/
  reference-app`. A playbook here needs a real, new Vue UI feature built
  first (a relying-party access request panel) — deliberately not built
  this pass, since inventing UI whose only purpose is to make a
  screenshot possible would invert this project's own "don't add
  features beyond what the task requires" rule. Revisit only if a real
  product reason for that UI surfaces independently of the playbook
  initiative.
- [ ] **Meridian's Workflow C SAR-escalation half needs a step-up
  authentication flow `Samples.Meridian.Seed` doesn't perform.**
  The periodic-screening half is now covered (`docs/playbooks/
  meridian/workflow-c-periodic-screening-and-sar-escalation.md`,
  screening only) — `SarFilingRecorded` itself
  (`MeridianWorkflowC.cs`) declares `RequiredSignature: ["urn:kyc:acr:
  step-up"]`, a real RFC 9470 step-up-authentication gate the seed
  script has no mechanism to satisfy. A second, independent, real
  finding from building that playbook: the registered `ApplicantIdentity`
  `ViewDefinition` template's own bound fields don't match `Sanctions
  ScreeningPerformed`'s payload shape at all, so only `applicantId`
  renders — `ScreeningDate`/`MatchFound`/etc. are genuinely published
  but never appear on screen through that template. Not fixed (a real
  content decision, out of this pass's scope) — flagged in the
  playbook's own caption and `docs/playbooks/README.md` instead of
  producing a silently misleading screenshot.
