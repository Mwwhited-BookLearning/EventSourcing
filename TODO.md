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

Direct request: add PlantUML sequence diagrams to every UI playbook, add
a per-application (Vitals/Meridian) README covering that application's
workflows and how they interact, rename every playbook file to a
`{role}-{task}.md` scheme (dropping the `{workflow}-{feature doc name}.md`
convention), and expand each application with more of the proving-ground's
own defined use cases. In progress, tracked here so nothing gets lost
mid-pass:

- [x] **Add a PlantUML sequence diagram to each of the 9 original
  playbooks** — done, all 9 (`VitalsWorkflowAPlaybookTests` through
  `MeridianWorkflowBRelyingPartyAccessPlaybookTests`). The 2 new
  queue-decision playbooks below get theirs inline as they're built (PI
  Queue done; KYC Analyst Queue still needs one).
- [x] **Restructure every playbook to `{domain}/{role}/{task}.md`** —
  done. Went through two revisions on direct feedback: first
  `{role}-{task}.md` (dropping `{workflow}-{feature doc name}.md`
  entirely), then role moved into its own directory segment rather than
  a filename prefix. All 10 playbooks (9 original + the new PI Queue
  one) generate at their final nested paths, verified together against
  a live `AppHost`. Every stale generated file/asset folder under an
  older name (`git rm` for the originally-tracked `workflow-*.md` set,
  plain `rm` for the untracked intermediate flat `{role}-{task}.md` set
  that never got committed) is gone. `docs/playbooks/README.md`'s
  catalog rewritten to the new paths, split into per-domain tables.
- [x] **Add 2 new playbooks using already-built Queue UI** — done, both.
  Genuine additional proving-ground use-case coverage that needed no new
  production UI, unlike the Relying-Party Access panel:
  1. Vitals' Principal Investigator Queue (`VitalsPiQueue.vue`) —
     `VitalsPrincipalInvestigatorQueuePlaybookTests`, verified against a
     live `AppHost`. Found and fixed a real, previously-undiscovered bug
     doing it: `publishClient.ts`'s RFC 9470 step-up-retry check read
     `body.error`, but `PublishEndpoints.cs`'s actual 401 response is an
     RFC 7807 `ProblemDetails` body with no `error` field at all (the
     real field is `title`) — every step-up retry through this client
     silently fell through to an ordinary failure before this fix. Also
     corrected `useEventComposer.spec.ts`'s own mock, which had been
     matching the bug's assumption, not the real server response.
  2. Meridian's KYC Analyst Queue (`MeridianAnalystQueue.vue`) —
     `MeridianKycAnalystQueuePlaybookTests`, verified against a live
     `AppHost` (accept/reject a pending `SanctionsScreeningPerformed`
     match `Samples.Meridian.Simulator` publishes every ~25s, alternating
     matches roughly 1 in 3 ticks). Found and fixed a second real bug in
     the same family: `AuthorityQueue.vue`'s own `summarize()` rendered a
     masked field (`MatchedName`/`MatchedListEntryId`'s `{value, masked,
     erased}` wrapper) as the literal, useless string `"[object Object]"`
     — the first time this queue was ever exercised with a masked field
     actually present in the payload. Fixed to match
     `EntityBrowser.vue`'s own already-correct `"[masked/complex]"`
     handling; new regression test added
     (`AuthorityQueue.spec.ts`).
- [x] **Vitals' Workflow C (Trial Data Export and Subject Rights)** —
  done: `LineageExportAndPlaybackPanel.vue`, a new domain-agnostic
  "Lineage & Playback" tab wiring `BitemporalPlaybackControl.vue`/
  `OfflineBundleViewer.vue` together with `exportLineage`/
  `downloadBundle`/`playbackAsOf` (all of which already existed, unused).
  `VitalsWorkflowCLineageExportAndPlaybackPlaybookTests` verified against
  a live `AppHost` — export, bundle verification, full event list, and a
  real System-Time Playback reconstruction all demonstrated for real.
  Found and fixed two more real bugs the same way as the queue playbooks'
  own two: `parseNdjson` (`bundle.ts`) never actually remapped the
  server's real PascalCase NDJSON output to the camelCase shape every
  downstream consumer assumed (a bare, unchecked type assertion) — every
  field was silently `undefined` until `verifyBundle`'s own date parsing
  threw; and `PlaybookRecorder.RecordStepAsync`'s screenshot was
  viewport-only, silently cropping this panel's own playback result
  (which sat below the fold on a page taller than one screen) despite
  its own visibility assertion passing — fixed to `FullPage: true`, all
  12 playbooks regenerated under it. The erasure half of this workflow
  (`EntityErasureRequested`) was not investigated this pass — worth
  checking whether the already-generic Event Composer tab already
  reaches it before assuming it needs its own UI too.
- [x] **Create `docs/playbooks/vitals/README.md` and `docs/playbooks/
  meridian/README.md`** — done. Each lists its own domain's workflows
  and playbooks, plus a PlantUML object diagram showing how they
  actually interact through shared entities: Vitals' four workflows
  around one continuity subject (`S-0091`, several loosely-related
  entities linked by business `SubjectId` fields, not `ADR-005` causal
  parent links); Meridian's three workflows all folding onto the exact
  same `ApplicantIdentity` entity, with Workflow B (Relying-Party
  Access) a genuine data dependency on Workflow A's own event, not just
  a shared subject. `docs/playbooks/README.md`'s own catalog now points
  to both rather than restating their content.

- [ ] **Create `style-guide.md` describing how `client-web`'s UI/UX
  should work** (direct request), with example screens either as
  PlantUML+Salt mockups or as real pages captured via a Playwright
  script that keeps the file updated (this project's own established
  `PlaybookRecorder` mechanism, reused). Deliberately sequenced AFTER
  the Naive UI/left-nav item below, not before: a style guide describing
  the TARGET UI/UX would need rewriting the moment that adoption lands
  if written against today's plain-HTML shell first. Not yet started.

- [ ] **Adopt Naive UI (`naiveui.com`) and a left-hand-nav shell
  (Azure Portal/Azure DevOps-style), replacing `client-web`'s current
  plain-HTML tab-button styling entirely — direct request, deliberately
  sequenced AFTER the diagram/rename/README/expansion work above, not
  alongside it.** Every Playwright playbook test currently in flight
  locates elements by role/label text against the CURRENT plain markup
  (`GetByRole(AriaRole.Button, new() { Name = "Browse" })`, etc.) — a
  navigation-pattern rewrite touches that same markup across every tab,
  so doing it mid-batch would mean re-verifying every playbook a second
  time, not once. Real work this item actually needs, not yet scoped in
  detail: (1) research Naive UI's own Vue 3 compatibility and pull in
  the library per `docs/libraries/README.md`'s existing "one file per
  adopted framework" convention (this project's own standing "buy over
  build," "verify before citing" rules apply to a new UI dependency the
  same as anything else) — no such doc exists for it yet; (2) redesign
  `App.vue`'s current top-nav tab-button shell into a left-hand
  navigation rail (collapsible sections, matching the Azure Portal/DevOps
  pattern named) — a real layout and routing-model change, not a drop-in
  style swap, since today's shell is a plain tab switcher with no router
  at all (ADR-039's own "no Vue Router dependency" decision may need
  revisiting, or may not — worth checking before assuming); (3) once the
  shell is rebuilt, every existing Vitest component test AND every
  Playwright playbook test's own selectors will likely need updating to
  match the new markup, in the same pass, not deferred; (4) decide
  whether `EntityBrowser.vue`/`GenericFallbackView.vue`/`EventComposer.vue`/
  the two Queue components/`RelyingPartyAccessPanel.vue` get restyled
  with Naive UI's own components (`n-table`, `n-form`, `n-button`, etc.)
  in the same pass or as follow-on work per component -- a real scope
  decision, not yet made.
