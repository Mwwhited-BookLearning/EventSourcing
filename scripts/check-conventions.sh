#!/usr/bin/env bash
# Phase 5 (docs/architecture-design-guidelines.md) -- the bespoke rules
# that aren't expressible as a real Roslyn/ESLint rule, so they get a
# small script instead: no external `!include` in any real PlantUML
# source (CLAUDE.md's own standing rule -- C4-PlantUML fails silently
# offline), and no `--` inside an XML comment in a .csproj file (a
# recurring MSB4025/MSB4024 mistake in this repo's own history -- this
# same session hit it three separate times while writing this tooling).
#
# Both checks are scoped deliberately narrow to avoid false positives:
# the !include check only looks inside real .puml files and fenced
# ```plantuml blocks, never prose that merely mentions the rule (an
# earlier draft of this script flagged every doc that talks ABOUT the
# ban, found and fixed before this script was ever committed); the --
# check strips the <!-- / --> delimiters themselves before looking for
# an embedded --, since the opening delimiter's own "--" was originally
# triggering a false positive on every single XML comment in the repo.
#
# Exit code reflects "warning" severity per direct request: reports
# every finding but exits 0 regardless, matching the .NET/ESLint
# baseline's own choice not to fail the build on these yet.
#
# Usage: scripts/check-conventions.sh
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

found_any=0

echo "==> Checking for external !include in real PlantUML source (.puml files and fenced \`\`\`plantuml blocks in docs/)"
puml_hits=$(grep -rn '!include' --include="*.puml" . 2>/dev/null | grep -v '/node_modules/' | grep -v '/bin/' | grep -v '/obj/' || true)
fenced_hits=$(find . -name "*.md" -not -path "*/node_modules/*" -print0 2>/dev/null | xargs -0 -I{} awk '
    /^```plantuml/ { in_block=1; next }
    /^```/ { in_block=0 }
    in_block && /!include/ { print FILENAME ":" FNR ": " $0 }
  ' {} 2>/dev/null || true)
include_hits="${puml_hits}${puml_hits:+$'\n'}${fenced_hits}"
if [ -n "${include_hits// /}" ]; then
  echo "$include_hits"
  echo "^ found !include in real PlantUML source -- CLAUDE.md's standing rule: hand-style C4 notation in plain PlantUML instead, never !include."
  found_any=1
else
  echo "  none found."
fi

echo ""
echo "==> Checking for '--' inside an XML comment in any .csproj file (recurring MSB4025/MSB4024 mistake)"
csproj_hits=$(find . -name "*.csproj" -not -path "*/bin/*" -not -path "*/obj/*" -print0 | xargs -0 -I{} awk '
    {
      line=$0
      gsub(/<!--/, "", line)
      gsub(/-->/, "", line)
    }
    /<!--/ { in_comment=1 }
    in_comment {
      if (index(line, "--") > 0) print FILENAME ":" FNR ": " $0
    }
    /-->/ { in_comment=0 }
  ' {} 2>/dev/null || true)
if [ -n "$csproj_hits" ]; then
  echo "$csproj_hits"
  echo "^ found '--' inside a .csproj XML comment -- this breaks the MSBuild XML parser (MSB4025/MSB4024). Use a single '-' or an em dash instead."
  found_any=1
else
  echo "  none found."
fi

echo ""
echo "==> Reminder: dotnet test invocations should use --logger \"console;verbosity=detailed\" (CLAUDE.md's own standing convention)"
echo "    Not mechanically checkable here (this checks files, not how a command was invoked) -- verify by eye in any"
echo "    documented test-running instructions (scripts/run-ci-local.ps1, docs/getting-started.md, README.md)."

echo ""
if [ "$found_any" -eq 1 ]; then
  echo "One or more conventions above were violated -- see details above. (Exit 0: warning severity, not enforced as a build failure yet.)"
else
  echo "All checked conventions clean."
fi
exit 0
