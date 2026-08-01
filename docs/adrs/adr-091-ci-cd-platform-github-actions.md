[← ADR index](../07-adrs.md)

# ADR-091: CI/CD platform — GitHub Actions, because that's where this repository lives; revisit if that ever changes

Status: Accepted

Context: `docs/10-open-questions.md` row 5 asked what CI/CD platform,
build→release→run separation, and dev/staging/prod promotion path this
framework's own build assumes. Narrowed twice already this session: the
publish *target* is settled (`ADR-062`, NuGet+npm); the *tool* choice
turned out not to be independent either — it's whatever's native to
wherever the repository is actually hosted. Direct instruction to close
this out: there's no code and no pipeline yet, so there's genuinely
nothing at stake in naming the tool now — write the ADR so the question
stops being asked, and revisit for real once this moves toward an actual
build.

Decision:
- **GitHub Actions** — this repository is hosted on GitHub today; that's
  the entire justification, not a comparative evaluation of CI
  platforms. If this repository is ever moved to a different host
  (GitLab, Azure DevOps, or otherwise), that host's own native CI is the
  answer instead — this ADR names today's fact, not a portable
  preference for GitHub Actions specifically over its alternatives.
- **Build→release→run separation and a dev/staging/prod promotion path
  are explicitly not designed here** — there's no real build to
  sequence yet. When one exists, that's new, substantive design work,
  not a mechanical extension of this ADR.
- **`ADR-080`'s existing requirement (GitHub Actions or GitLab CI/CD for
  npm provenance) is satisfied by construction**, not coincidentally —
  the platform this ADR names is one of the two `ADR-080` already
  required.

Consequences:
- If this repository ever moves hosts, this ADR needs a real revision
  (`.claude/protocols/additive-history-editing.md`'s strikethrough rule)
  naming the new host's native CI — not a silent assumption carried
  forward.
- No pipeline, workflow file, or build-plan phase is created by this
  ADR — it resolves the *question*, not the *build*.
- Resolves `docs/10-open-questions.md` row 5.
