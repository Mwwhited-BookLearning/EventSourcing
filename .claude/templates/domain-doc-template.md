# Template: proving-ground domain doc (`docs/domains/{slug}/README.md`)

Real examples to diff against: `docs/domains/clinical-trials-device-
telemetry/README.md` (chosen domain, deep), `docs/domains/biobanking/
README.md` (considered-not-chosen, lighter).

## Skeleton

```
[← Domains index](../README.md)

# Domain: {Name}

**Status:** {Chosen proving-ground domain | Considered, not chosen} —
see `docs/comparisons/proving-ground-domain.md` for the full comparison.

## Overview
<2-4 sentences: what real-world system this domain represents, and why
it was picked/considered as a proving ground for this framework
specifically>

## Governing regulations/standards
| Framework | What it governs here |
|---|---|
<one row per real regulation, verified before citing>

## Applicable ADRs
**Primary fit (the domain's defining characteristics):**
- `ADR-NNN` — <one-line reason this is load-bearing here, not just
  applicable>

**Secondary fit (real, but not the domain's defining characteristic):**
- `ADR-NNN` — <one-line reason>

## Special concerns
- <a real tension, a weak spot, or a standout fit — not padding. Each
  bullet should be something a reader building against this domain
  actually needs to know before they start>

## Glossary
- **Term** — plain-language definition, verified before writing. Where
  a genuine tie-in exists to an ADR already in this file's Applicable
  ADRs list, a closing clause naming it (e.g. "— modeled here as an
  Entity, `ADR-021`"). Alphabetized. Do NOT duplicate generic Duplex
  engine terms already in `docs/glossary.md` — this section is that
  industry's own jargon only.
```

Do not put entity/event structures, state-machine diagrams, or Salt
mockups directly in this file — those go in
`docs/domains/{slug}/features/*.md`, using
`.claude/templates/feature-doc-template.md`. This file stays a
reference doc (regulations/ADRs/concerns/glossary); the feature docs
are where a domain gets worked all the way through as a concrete
example.

## Rules

- **Verify every regulation/standard/synonym before writing it down.**
  Never recall from memory and assume correct — this project's most
  repeated, most explicit standing convention.
- **A synonym is only worth adding if it's a TRUE synonym, not
  related-but-distinct.** Check carefully — KYC/CDD, Clearinghouse/CCP,
  Legal Hold/Litigation Hold all looked like synonyms and weren't. If
  related-but-different, say so explicitly rather than silently
  omitting or falsely equating.
- **Cross-check against `docs/comparisons/proving-ground-domain.md`**
  (the authoritative coverage matrix + regulatory mapping) before
  claiming an ADR applies or a regulation governs — this file is
  *generated from* that comparison, not an independent source.
