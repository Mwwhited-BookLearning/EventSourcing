[← ADR index](../07-adrs.md)

# ADR-073: Accessibility standard — WCAG 2.1 AA baseline, WCAG 2.2 AA forward-looking

Status: Accepted

Context: A proving-ground compliance review (this session) found
accessibility tagged only to the government-case-management candidate's
`Section 508` citation — too narrow. Accessibility is a property of
*whichever UI technology actually renders a screen*, not a specific
architecture — it applies identically whether that screen is `ADR-039`'s
MVVM client, or a fallback to MVP/MVC/code-behind for a screen `ADR-039`
doesn't fully dictate (`docs/comparisons/ui-architecture-patterns.md`'s
own stated fallback chain). It earns its own ADR rather than living
inside `ADR-039` specifically, so the requirement doesn't read as
conditional on one UI pattern.

**Checked against the real standard and its actual legal status, not
assumed**: the US DOJ's ADA Title II final rule requires **WCAG 2.1
AA** of government entities, with an April 2026 compliance deadline for
larger entities already in effect as of this writing — not a future or
hypothetical requirement. WCAG 2.2 is the current published W3C
standard, but most legal enforcement (including that same DOJ rule)
still cites 2.1 AA specifically, not 2.2.

Decision:
- **WCAG 2.1 AA is the baseline accessibility standard for every screen
  this framework's client renders**, regardless of which UI pattern
  (`ADR-039`'s MVVM primary, or a named fallback) actually implements a
  given screen — not scoped to government case management or any other
  single proving-ground domain, since every domain's client renders
  through the same client-technology stack.
- **WCAG 2.2 AA where practical is the stated forward-looking
  position** — building to the newer standard now costs little extra
  over 2.1 AA and avoids a second migration later, even though 2.1 AA
  remains the legally-cited bar today.
- **This ADR governs the requirement; `ADR-039` (and any fallback UI
  pattern) governs how it's satisfied** — a deliberate separation, not
  a redundant one: which specific components/patterns achieve WCAG
  conformance is an implementation detail of whichever UI architecture
  is active for a given screen, not a decision this ADR needs to make.

Consequences:
- Resolves the accessibility half of the proving-ground compliance
  review's findings — `docs/comparisons/proving-ground-domain.md`'s
  regulatory mapping table now states this as cross-cutting, not
  per-domain.
- No UI pattern decided so far (`ADR-039`, or the fallback chain in
  `docs/comparisons/ui-architecture-patterns.md`) is blocked by or
  contradicts this — WCAG 2.1 AA conformance is achievable in MVVM,
  MVP, MVC, or code-behind alike; this ADR doesn't favor one over
  another.
- `06-solution-structure.md`/build-plan exit criteria should eventually
  include an accessibility conformance check per screen — not yet
  detailed, flagged as remaining propagation work alongside this
  session's other new-ADR build-plan gaps.
