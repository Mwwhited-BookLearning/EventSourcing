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

The large, dependency-ordered phase structure this file used earlier in
the session (GraphQL/API-contract cluster → build-plan restructuring →
13-domain Salt-mockup batch) is now fully worked through — see
`docs/changes/2026-08-01.md` and `docs/changes/2026-08-03.md`. What's left
is three unrelated items found mid-session, each independent of the
others; pick whichever suits available time.

- [ ] **`docs/features/masking.md`'s wrapper is still v1's original
  two-branch `FixedValue`-only shape** (`{value}`/`{masked}`); `ADR-057`
  later added a third `erased` branch (crypto-shredding), already
  reflected in `03-api-contracts.md`'s GraphQL schema (`{ value masked
  erased }`) but not propagated into this doc's scenarios/ER diagram —
  a bigger lift than the mechanical fixes this pass covered, deliberately
  left for its own pass. **Bundled with a related, found-but-unresolved
  contradiction**: `masking.md`'s own prose says v1 supports exactly one
  masking strategy (`FixedValue`), with a scenario asserting `PartialReveal`
  is rejected at registration, but `06-solution-structure.md` shows three
  registered strategies (`FixedValueMaskingStrategy`,
  `PartialRevealMaskingStrategy`, `HashMaskingStrategy`) — resolve which
  ADR (if any) actually expanded masking beyond `FixedValue` before fixing
  either.
- [ ] **`docs/features/*.md` has zero coverage for `ADR-054`–`074`** —
  no dedicated feature doc (or section of an existing one) exists yet for
  rate limiting (`ADR-058`), outbound webhooks (`ADR-060`), client SDK
  generation (`ADR-054`), device input integration (`ADR-070`), leader
  election (`ADR-078`), dynamic feature flags (`ADR-077`), i18n/l10n
  (`ADR-087`), mechanism-level OTel instrumentation (`ADR-088`), Event Log
  archival (`ADR-089`), tenant federation (`ADR-082`), RFC 3161
  timestamping (`ADR-086`), or secret rotation (`ADR-093`). Genuinely
  separate from the item above (new content, not fixing stale content) —
  split out rather than silently folded in or dropped. Needs its own
  scoping pass (which of these earn a full feature doc with Gherkin vs.
  a section added to an existing one) before starting.
- [ ] **`RequiredPublishClaim`/`RequiredReadClaim` (pre-`ADR-050`
  singular-claim naming) found still presented as the *current* field
  name — not historical narration — well beyond `docs/features/*.md`**:
  at minimum both feature docs of the digital-identity-kyc domain,
  `itar-export-controlled-defense-data`, `education-credentials`,
  `pharmacovigilance`, `insurance-telematics`, `government-case-
  management`, and three of clinical-trials-device-telemetry's feature
  docs; `docs/05-schema-registry-and-spec-generation.md` (its own banner
  already names this, but the body itself was never actually rewritten);
  `docs/adrs/adr-013-problem-details.md`'s error-response table (still
  says "missing `RequiredPublishClaim`/`RequiredReadClaim`" and a
  `$filter`-undeclared-field row that's also stale per `ADR-037` — note
  ADRs are additive-history, `.claude/protocols/additive-history-
  editing.md`, so this needs a superseding note, not a silent rewrite);
  plus `docs/data/streaming-and-attachments.md`'s
  `TelemetryChannel.RequiredReadClaim` field (channels were never folded
  into `ADR-050`'s generalization — confirm whether they should be before
  fixing, since channels aren't `EventTypeDefinition` rows). Found while
  fixing `06-solution-structure.md`'s and several `docs/features/*.md`
  files' own instances of the same staleness — a full repo grep is needed
  to scope this fully; not attempted yet, likely its own parallel-dispatch
  batch given the file count.
