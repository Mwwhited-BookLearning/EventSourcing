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
- ~~No pipeline, workflow file, or build-plan phase is created by this
  ADR — it resolves the *question*, not the *build*.~~ **Corrected, later
  pass**: a build-plan phase and the real files now both exist —
  `docs/08-build-plan.md`'s item 39 ("Release Engineering, Packaging &
  Supply Chain") is marked Done, and built
  [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) and
  [`.github/dependabot.yml`](../../.github/dependabot.yml) to realize
  this ADR's own decision. This resolves the *question* (as this ADR
  always intended) and now also has a real build to go with it — but not
  full closure: no push access to GitHub exists in this environment, so
  the workflow file has been written but never actually executed by
  GitHub Actions itself. "The build now exists too, though unexecuted,"
  not "verified running in CI."
  **Corrected again, 2026-08-11, direct request, a further narrowing
  not a reversal**: `ci.yml` no longer includes SBOM generation
  (`ADR-074`) or build-provenance attestation (`ADR-080`) at all — those
  moved to a local-only script, `scripts/generate-sbom.sh`, at this
  time. `ci.yml` now covers only the test-suite/vulnerability-scan half
  of item 39's exit criteria; the SBOM/provenance half is proven working
  as a standalone local command, deliberately not (yet) wired into any
  CI platform. This doesn't touch this ADR's own Decision (GitHub
  Actions is still the CI/CD platform, when one is used) — only how
  much of item 39's *scope* currently runs through it.
- Resolves `docs/10-open-questions.md` row 5.
