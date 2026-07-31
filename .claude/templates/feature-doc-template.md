# Template: feature doc (`docs/features/*.md` and `docs/domains/{domain}/features/*.md`)

Copy this skeleton for any new feature/use-case doc. Real example to
diff against: `docs/features/entity-concept.md` (has a real Salt
mockup: `docs/features/mvvm-client.md`).

## Required sections, in this order

1. **Title** — `# Feature: {Name}`
2. **Context paragraph** — which ADR(s) this exercises, which data-model
   doc(s) define the entities involved, and an explicit **out-of-scope
   list**: name what this doc deliberately does NOT re-derive (a merge
   rule already shown elsewhere, a claims-check already covered by
   `auth.md`, etc.) with a pointer to where it *is* covered. Every real
   feature doc in this repo has this — it's what keeps 20+ docs from
   each re-explaining the same mechanism.
3. **One or more sequence diagrams** (`## Sequence diagram — {label}`),
   PlantUML, `@startuml`/`@enduml`, `autonumber` on. Show the real
   participants (endpoint, background service, database) and real
   request/response shapes — not abstract boxes. Use `alt`/`else` for
   genuinely different branches (success vs. conflict vs. validation
   failure), not one diagram per branch.
4. **Data model (ER diagram)** (`## Data model (ER diagram)`), PlantUML
   `entity`/`database`, `hide circle`, `skinparam linetype ortho`. Show
   only the columns this doc's scenarios actually touch — full column
   lists live in `docs/data/*.md`, linked, not repeated.
5. **Salt (UI mockup)** (`## Salt (UI mockup)`) — a **user flow across
   multiple screens**, not one static mockup: 2-4 separate
   `@startsalt`/`@endsalt` blocks in sequence, each labeled ("Screen 1:
   ...", "Screen 2: ...") with a one-line description of the user
   action that transitions between them (a button click, a form
   submit, a role handoff). Ground each screen in a real step the
   doc's own sequence diagram(s) already show — don't invent a flow the
   rest of the doc doesn't support. If there's genuinely no UI surface
   for this feature, say so explicitly and name what doc *does* own the
   eventual UI, same as `entity-concept.md` does — never just omit the
   section silently.
6. **Gherkin** (`## Gherkin`), one fenced ` ```gherkin ` block, embedded
   directly in this doc — **not** a separate `.feature` file** (per
   direct instruction: Gherkin stays embedded in the design doc until
   implementation actually starts; extraction to real `.feature` files
   happens then, not before). Structure:
   - `Feature:` line matching the doc title.
   - A `Background:` registering whatever schema/state every scenario
     needs, so individual scenarios don't repeat setup.
   - One scenario per real branch/edge case the sequence diagram(s)
     show — not padding. Prefer named literals (`"demo:Order:o-1"`) over
     placeholders. A `#`-prefixed comment under any scenario whose
     "why" isn't obvious from the Given/When/Then alone.

## Non-negotiable conventions (violating these breaks consistency with
every other doc in the repo)

- Every diagram is **plain PlantUML** — no `!include` of any kind, ever
  (not `C4-PlantUML`, not anything fetched from a URL or a bundled
  stdlib path). See `CLAUDE.md`'s standing convention and
  `references.md`'s `C4-PlantUML` reference-only entry for why: it fails
  silently in any renderer without live internet or that exact stdlib
  path configured.
- Cite the ADR that decided a mechanism inline, in prose, every time
  it's used (`` `ADR-023` ``) — never assume a reader has the number
  memorized from an earlier section.
- If this feature doc lives under `docs/domains/{domain}/features/`,
  ground every entity/event in that domain's own `README.md`
  ("Applicable ADRs" section) — don't invent a mechanism the domain doc
  doesn't already list as applicable.
