# Protocol: filing a bug report in `docs/bugs/`

## What this is for

CLAUDE.md's own standing rule is that **a build/test pass succeeding is
not the same bar as actually running the thing** — this session alone
found real, previously-undiscovered defects only by running a real
`AppHost`, a real Playwright playbook, or reading a real Postgres
container's own logs (the concurrent-writer hash-chain corruption, the
RFC 7807-vs-OAuth-shape mismatch, the `"[object Object]"` masked-field
render, the PascalCase-vs-camelCase NDJSON bug). Each of those was
narrated once, in that day's `docs/changes/{date}.md`, and then
effectively lost — findable only by reading that day's prose top to
bottom, not by symptom, component, or tier. (A routine-Postgres-40001
investigation the same session found real root cause but no safe fix
yet — tracked in `TODO.md`, not here, since this protocol is for
defects that are actually *fixed*; see that item for why two plausible-
looking fixes both failed.)

`docs/bugs/{scope}/{tier}/{summary-title}.md` is the durable, indexed
record a changelog entry doesn't provide: **one file per real bug, filed
the same pass it's fixed**, organized so a later session (or a later bug
that smells similar) can actually find it.

**Not for**: a design fork (`docs/10-open-questions.md`), a doc/code
drift with no runtime defect (`TODO.md`), or a decision already made
that just needs doing (`TODO.md`). This is specifically for a genuine
defect — code that actually behaved wrong — found and fixed.

## Where it goes

`docs/bugs/{scope}/{tier}/{summary-title}.md`. `{summary-title}` is a
short kebab-case slug, same style as an ADR's own filename slug (e.g.
`masked-field-renders-as-object-object.md`, not a full sentence).

`{scope}` is `framework` or `proving-ground` — direct request, kept as
its own top-level split rather than folded into `{tier}`, matching this
repo's own standing framework-vs-proving-ground distinction
(`docs/domains/` vs. everything else): a defect in core Duplex
(`src/EventStore.*`, `client-web/packages/mvvm-client/**`'s generic
layer) is `framework`; a defect specific to Vitals/Meridian or their own
`client-web/packages/reference-app` components/tests is
`proving-ground`. If a bug is genuinely proving-ground-domain-specific
(Vitals-only vs. Meridian-only) rather than shared, that's a further
split worth adding under `proving-ground/` the same way a new `{tier}`
gets added below — not built speculatively until a bug actually needs
it.

`{tier}` is the layer the *defect* actually lived in, not the layer
where it was *observed* (a UI symptom caused by a service-tier bug files
under `service`, not `ui`) — pick whichever the fix's own file paths
land in:
- `ui` — Vue component markup/rendering (`client-web/packages/
  reference-app/src/components/**`)
- `client` — client-side logic below the markup: composables, API
  clients, stores, `client-web/packages/mvvm-client/**`
- `service` — .NET backend: Hosts, Workers, GraphQL, Inbox/Router,
  anything under `src/EventStore.*`/`src/Samples.*`
- `database` — persistence, migrations, EF Core, raw SQL, concurrency/
  isolation (`src/EventStore.Persistence*`, provider migration
  projects)
- `test` — the test harness itself misbehaving (e.g. a screenshot
  helper silently cropping a passing assertion's own result), not a
  test that correctly caught a real bug elsewhere
- Add a new tier folder the moment a bug genuinely doesn't fit one of
  these — same "don't force a bad fit" principle as this repo's other
  taxonomies (`docs/domains/`'s own considered-not-chosen list, the
  masking-guardrail's own cardinality tiers). Note the new tier in this
  protocol file's list above in the same pass, so it doesn't silently
  diverge from what's actually on disk.

## What the file contains

No fixed template file exists yet (unlike ADRs' `.claude/templates/
adr-template.md`) — write these four sections, in order, plain
prose/bullets like a changelog entry, not a rigid form:

1. **What was wrong** — the observed symptom, concretely (an error
   message, a rendered value, a failed assertion), not a vague
   category.
2. **How and where it was found** — the actual diagnostic path: which
   real thing was run (a live `AppHost`, a specific Playwright playbook,
   a `docker logs` read, a user report reproduced), not "found during
   review." If a technique was reusable (e.g. a temporary response-
   body-dumping handler, reading a container's own logs instead of
   relying on an attached debugger), say so explicitly — that's often
   the most valuable part for the next similar bug.
3. **Root cause** — the actual mechanism, precisely, the same
   rigor `docs/changes/*.md` entries already use (e.g. "EF's change
   tracker still believed X" not "there was a caching issue").
4. **Resolution** — the fix (file:line or file names), and how it was
   verified — **name the actual regression test** (see "The regression
   test" below), not just "tests pass."

Cross-link both directions: the bug file cites the ADR/doc that
describes the correct behavior it violated (if any), and cite the bug
file from wherever else would otherwise re-describe it in prose (see
below).

## The regression test (required, direct request)

Every bug report is filed alongside a test that **reproduces the bug
itself**, not just one that exercises the surrounding feature:

- It must actually **fail (red) against the pre-fix code and pass
  (green) against the fix** — proven, not assumed. If the fix is
  already committed, verify red by temporarily reverting just the fix
  (e.g. comment out the one call/line that changed) and re-running the
  test before restoring it, the same "confirmed by actually reverting
  it locally" rigor `RetryOnFailurePostgresTests.cs`'s own comments
  already use elsewhere in this repo.
- Use an existing test file/class covering the same component if one
  exists (matching this repo's existing suite structure); add a new one
  only if nothing already covers that area.
- Tag the test with a trait referencing the bug report, so the
  relationship holds in code, not only in prose:
  `[TestProperty("BugReport", "docs/bugs/{scope}/{tier}/{summary-title}.md")]`
  (MSTest's `[TestProperty]`, the same mechanism `TODO.md`'s own test-
  trait/category tracking item is standardizing more broadly — this is
  that convention's one always-required key, usable today independent
  of whether the fuller categorization sweep has happened yet).
- **Reference the test from the bug report's own "Resolution" section**
  by its fully-qualified name (class + method), so the file and the code
  point at each other both ways.

## Relationship to `docs/changes/{date}.md` and `TODO.md`

Same "don't restate, link instead" principle this repo already applies
to `docs/10-open-questions.md` and `TODO.md` themselves:

- **`docs/bugs/**` is the durable, detailed record.** Write the full
  four-section account there.
- **`docs/changes/{date}.md`'s own entry becomes a short pointer**, not
  a second full narrative: what happened, one or two sentences, then
  "see `docs/bugs/{scope}/{tier}/{summary-title}.md`" — matching how a `TODO.md`
  item's completion note already points at that day's changelog instead
  of restating the change.
- If a `TODO.md` item's own completion turns up a real bug along the
  way (the common case — most of this session's bugs were found while
  building or verifying a `TODO.md`/build-plan item, not on their own),
  file the bug report in the same pass the item is marked done, and
  have the `TODO.md`-completion changelog line mention it too.

## Filing one

1. Fix the bug.
2. Write (or confirm) the regression test per "The regression test"
   above, including proving it actually goes red without the fix.
3. Pick `{scope}` (`framework`/`proving-ground`) and the tier from the
   list above (or add a new one, noting it here).
4. Write `docs/bugs/{scope}/{tier}/{summary-title}.md` per the four
   sections above, naming the test in "Resolution."
5. Add the short pointer line to today's `docs/changes/{date}.md` (per
   `todo-tracking.md`'s own existing rule to always update that file
   when finishing work).
