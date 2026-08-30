# Project Context (session handoff)

**This is a snapshot, not a log — overwrite it in place each session,
don't append to it.** History lives in `docs/changes/{date}.md`; open
forks live in `docs/10-open-questions.md`; active doc-tracker tasks live
in `TODO.md`; active *implementation* status lives in
`docs/08-build-plan.md`'s "Implementation status" table. This file exists
so a fresh agent (or a human) can resume from the repo alone, without
replaying git-log archaeology or losing information the way an earlier,
unresumable conversation did. See `.claude/protocols/context-handoff.md`
for the update rules — in particular, narrative history of *completed*
work belongs in `docs/changes/{date}.md`, not here; this file drifted
into a multi-thousand-line duplicate of that history once already
(purged 2026-08-13, direct request — "move the important content to the
correct files... reference those files and purge the garbage"). Don't
let it happen again: if you're about to write more than a sentence or
two about *what got built*, that sentence belongs in today's
`docs/changes/{date}.md` instead, with only a pointer left here.

## What this project is

`EventSourcing` (repo name is a known typo for `EventSourcing` — see
`CLAUDE.md`, deliberately not yet renamed) is a from-scratch design
**and, since 2026-08-03, a real implementation** for an event-sourcing
store ("Duplex," `docs/naming.md`), built as a **worked teaching
example**: append-only write side (schema registry, publish/follow/
lineage APIs, GraphQL-only query layer), a CQRS read side, and two fully
worked proving-ground domains (clinical trials + device telemetry —
"Vitals"; digital identity/KYC — "Meridian"). Governing principle: never
lose or corrupt data.

## Current state

*(as of 2026-08-29, branch `dev/cryptoshredding` off `main` — update this
whole section, don't just bump the date)*

All 100 ADRs Accepted (`ADR-100` added this session, configurable
presentation-type/charting). `08-build-plan.md`'s item 54/55/56 status is
unchanged from the paragraph below (not touched this pass — this
session's work was entirely `TODO.md` items, not build-plan items).
`client-web`'s reference app grew from one generic entity view into 8
tabs across 12 real, Playwright-verified playbooks
(`docs/playbooks/README.md`); `ADR-099` then replaced its plain-HTML
tab-button shell with Naive UI + Vue Router behind a left-hand-nav rail,
restyling every existing component in the same pass; `docs/style-
guide.md` was written from real captured pages of that shell.

This session (2026-08-29, full narrative in `docs/changes/2026-08-29.md`)
worked every remaining *decided* `TODO.md` item, one at a time, per
direct instruction ("all the decided things can be worked one at a
time"): a real paged/filtered server-side entity-list GraphQL query
(replacing the old always-subscribe Browse pattern); root-caused and
fixed the Postgres `40001` serialization-error noise after two earlier
documented failed attempts (transaction-scoped `pg_advisory_xact_lock` +
Read Committed isolation, Postgres-only — see
`docs/bugs/framework/database/postgres-routine-40001-serialization-noise.md`,
the first bug report filed under the new bug-report-tracking protocol);
adopted Apache ECharts for configurable-presentation-type charting
(`ADR-100`, `docs/comparisons/charting-library.md` — catching a real
ApexCharts dual-license disqualifier and a real CSS/testability bug
chain only visible via an actual Playwright screenshot, not Vitest);
extended `JsonSchemaInstanceValidator` twice with real, spec-verified
JSON Schema Draft 2019-09+ keywords (string/number constraints + `enum`
+ `format`, then `dependentRequired`/`if`-`then`-`else`/`const`) —
finding and correctly handling a real `ADR-038` `x-enum-fallback`
compatibility conflict before it could regress an existing test; and
extended `ADR-007`'s derivation mechanism with calculated fields
(`SelectField.Expression`, evaluated via the already-adopted
`IUpcastExpressionEvaluator`/`ADR-053` seam — reused, not a third
expression mechanism); and, after explicitly confirming the flagged
tradeoff with the user first, built the PlantUML `.puml`-extraction +
Docker-`.svg`-rendering pipeline (`scripts/extract-diagrams.mjs`,
`scripts/render-diagrams.mjs`) — every diagram's fenced block stays
inline, untouched, with a rendered `docs/diagrams/**/*.svg` image
reference inserted above it, so the doc stays plain-text readable *and*
actually renders on GitHub. Found and fixed 6 genuine, previously-
undiscovered PlantUML syntax bugs only a real render caught (escaped
quotes, a 3-participant `note over` this PlantUML build rejects, an
`actor` mixed into an object diagram, literal braces nested inside a
Salt cell's own quoted string, one doc missing `@startuml`/`@enduml`
outright) — see `docs/changes/2026-08-29.md` for the exact list and
fixes. **`TODO.md` is now fully done — every item, including the DSL
fork's own tracker row in `docs/10-open-questions.md`, is either closed
or (that one row) explicitly still an open, undecided fork, not
forgotten work.** The diagram pipeline isn't wired into CI (not asked
for); re-running both scripts after editing any diagram is a real,
currently-manual step to keep the checked-in `.svg` in sync, flagged as
such in `TODO.md` rather than assumed automatic. A fresh session should
ask the user what's next rather than assume more `TODO.md` work exists.

This session (full narrative split across three passes in
`docs/changes/2026-08-27.md`): designed searchable equality/range
queries over `ADR-057`'s crypto-shredded fields (comparison doc + `ADR-
096`/`097`/`098`), then built and tested items 54/55 (`PayloadIndexer`,
`EncryptedFieldIndexEntry`, `OrderRevealingEncryption`, async
`GraphQlFilterPredicateBuilder` routing — **found and fixed a real
prerequisite bug**: `PayloadEncryptor`/`ADR-057`'s own encryption was
never wired into any Host's DI before this session, inert in
production until now), then built `ADR-098`'s two native predicate
evaluators. New code lives in `src/EventStore.Domain/SchemaRegistry/`,
`src/EventStore.Abstractions/`, `src/EventStore.Erasure/`, and a new
`net48` project `src/EventStore.SqlClr.SqlServer/` (SQL Server's CLR
host never loads .NET Core/.NET 5+, a real confirmed constraint — the
one deliberate break from this solution's otherwise-uniform net10.0
targeting). Migrated across all three providers. Verified: new unit/
integration tests including a genuine cross-runtime check (the SQLCLR
decrypt logic tested against a golden ciphertext fixture generated from
the real net10.0 `EnvelopeAesGcm.Encrypt`), the full pre-existing Sqlite
suite (150/150), and `ErasurePostgresTests`/`ErasureSqlServerTests`
against real Testcontainers Postgres/SQL Server — no regressions found
anywhere.

`ISearchIndexKeyStore`'s cloud/Vault gap is also now closed:
`CloudSearchIndexKeyStoreAdapter` wraps any existing `IErasureKeyStore`
cloud backend (Azure Key Vault/AWS KMS/Google Cloud KMS/HashiCorp Vault)
into a search-index key store via the same derivation trick `PerEntity`
scope uses, rather than a fourth bespoke SDK integration — verified
against `LocalErasureKeyStore` (provider-agnostic logic).

## Actively in flight

`TODO.md` has four items, added while reviewing Vitals/Meridian for real
`x-masking-searchable` candidates: a real guardrail gap found (`ADR-096`'s
cardinality check only gates `Range`, not `Equality`, though the same
paper it cites names deterministic encryption — what `Equality` is — as
frequency-analysis-vulnerable too), plus three domain-doc propagation
items (Vitals' `Patient Enrollment`, Meridian's `Customer Onboarding`,
Meridian's `Document Capture`/`Periodic Screening`). See `TODO.md` for
the concrete file-by-file detail, not repeated here.

Also still open, not yet in `TODO.md` (build-plan sequencing calls, not
"decided, just undone" doc/fix items): item 55's required security
review; item 56's PostgreSQL `plpython3u` function is written but
unverified (needs a custom Postgres image); both item 56 evaluators stay
`Local`-backend-only regardless of the cloud `ISearchIndexKeyStore` work
(the evaluators themselves would still need their own network access to
a real KMS/Vault). `dev/cryptoshredding` has 37 commits ahead of `main`
as of 2026-08-29 (pushed incrementally through this session, one
isolated unit of work per commit, per direct standing instruction), not
yet merged or opened as a PR — a fresh session should confirm with the
user before assuming either is wanted.

## How to resume cold

1. Read `CLAUDE.md` (standing conventions + doc-type index), then this
   file.
2. `docs/08-build-plan.md`'s "Implementation status" table, `TODO.md`,
   `docs/10-open-questions.md` — confirm they still match what this file
   claims above; if not, something changed without this file being
   updated (fix that first).
3. `git log --oneline -10` and `git status`.
4. Skim the latest `docs/changes/{date}.md` for the most recent session's
   narrative.
5. `dotnet build EventStore.slnx` and `dotnet test tests/EventStore.
   IntegrationTests` (needs Docker running for Testcontainers-backed
   PostgreSQL/SQL Server tests, and the SDK pinned in `global.json`). A
   full multi-provider run has known, pre-existing, unrelated
   load-induced flakiness under MSTest's parallelism (SQL Server/SQLite
   test classes occasionally failing container/file-cleanup races under
   host contention) — re-run the specific failing class alone before
   assuming a real regression; see this file's own "purged" note above
   for where the fuller flakiness history now lives
   (`docs/changes/{date}.md`, not here).
6. `client-web/`: `npm test` (`vitest`), `npm run build`, and `npm run
   build:offline-player`.

## Working notes not yet written down elsewhere

- **The user wants to be asked before large, effort-heavy content
  rewrites get started unilaterally — offer explicit options, don't just
  do it.** Smaller, unambiguous fixes (broken links, typos, a stale
  field name, a wrong library choice found mid-task) are fine to fix
  directly without asking first.
