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

Ten items surfaced by this session's from-scratch rework of
`docs/08-build-plan.md` (re-deriving all 48 items directly from their
ADRs found these as real doc/coverage gaps, distinct from the build-plan
corrections themselves, which were fixed in place — these are documented
here per the standing "don't let a found gap silently disappear"
convention):

1. `ADR-087`'s i18n/l10n requirement (translation-key discipline, CSS
   logical properties) was never actually written into
   `docs/features/mvvm-client.md` — that file only has an Accessibility
   section. `ADR-087` itself already flags this as undone propagation
   work; the build-plan's "i18n/l10n Architectural Scope" item's exit
   criteria are grounded directly in the ADR instead.
2. `docs/features/lineage-export-and-playback.md`'s `ExportManifest` ER
   class has no `RFC3161Timestamp` field and no Gherkin scenario, even
   though `03-api-contracts.md` already documents the export bundle
   carrying an RFC 3161 timestamp (`ADR-086`).
3. No Gherkin scenario anywhere under `docs/features/` exercises
   `RoleGranted`/`RoleRevoked`/`PermissionGranted`/`AppTrustRootRegistered`
   publication or replay-rebuild (`ADR-067`) — only `auth.md`'s prose
   describes the fold relationship.
4. `docs/features/streaming-channels.md` has no scenario for `ThreadId`-
   grouped multi-channel sessions or `RedactedRange` substitution
   behavior (`ADR-081`/`ADR-052`), despite the build-plan's "Streaming
   Channels" item citing both as exit criteria.
5. No dedicated feature doc exists for DPoP or hash-chained tamper-
   evidence verification specifically (`ADR-017`/`ADR-019`, "Hardening &
   Evolution"), nor for upcast materialization/downcast (`ADR-027`/`028`/
   `053`, "Upcast Materialization + Downcast").
6. `ADR-038` decides four things (enum-unknown-value fallback, version-
   discovery capability negotiation, Expand/Contract migration
   discipline, the N-1/N+1 rollback window); only the last has an actual
   exit-criteria scenario ("Compatibility & Deployment Discipline"'s
   rollback drill). The other three have no feature-doc coverage.
7. Binary Attachments' GraphQL-browse exit criterion and Sharding &
   Replication's cross-shard-fan-out exit criterion both have an
   undeclared forward dependency on "GraphQL-Only Query Layer" (sequenced
   after both) — re-verify each once that item lands rather than treating
   them as already fully testable at their own point in the build.
8. `ADR-009`'s `revealOnDemand` mechanism names its reveal action as a
   dedicated GraphQL `revealField(...)` operation, but neither "Property-
   Level Masking" nor "GraphQL-Only Query Layer" currently claims building
   it — it has no home in the build plan.
9. `docs/features/entity-concept.md` has no scenario exercising
   `LateArrivalFlag` specifically (only `ConflictFlag`), despite "Entity-
   Centric Core Rebuild"'s own exit criteria citing one.
10. A background agent doing this session's build-plan rework reported
    receiving a mid-task instruction to "create a
    `.\docs\develop\{epic}\{feature}.md`" — arrived truncated, no defined
    epic/feature taxonomy, source unclear (surfaced to the user, not yet
    clarified or acted on). Revisit once the user confirms what this was
    meant to be.
