#!/usr/bin/env bash
# ADR-074/080 -- SPDX 2.2 SBOM generation + build provenance, as a LOCAL,
# on-demand script rather than a GitHub Actions job, at direct user
# request (this session) -- the same "local scripts for POC/PoV are
# perfect" posture already applied to scripts/generate-migration-bundle.sh
# (ADR-076). `.github/workflows/ci.yml` deliberately no longer runs
# sbom-tool or a provenance-attestation step; build-and-test and the
# vulnerability-scan job are the only jobs that remain there.
#
# Usage: scripts/generate-sbom.sh [package-name] [package-version] [package-supplier]
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_name="${1:-Duplex}"
package_version="${2:-0.1.0}"
package_supplier="${3:-OoBDev}"

if ! command -v sbom-tool >/dev/null 2>&1; then
  echo "sbom-tool not found -- installing (dotnet tool install --global Microsoft.Sbom.DotNetTool)" >&2
  dotnet tool install --global Microsoft.Sbom.DotNetTool
fi

echo "Packing every EventStore.* NuGet package (ADR-062)..."
dotnet pack "$repo_root/EventStore.slnx" -c Release -o "$repo_root/artifacts/packages"

# -nsb (namespace base) is SBOM identifier scaffolding, not a real
# published URL -- left as a local placeholder rather than a hardcoded
# GitHub link, since this script has no dependency on this repo actually
# being hosted anywhere in particular (ADR-091's own "no code and no
# pipeline yet" framing).
echo "Generating SPDX SBOM covering both the NuGet and npm dependency graphs..."
sbom-tool generate \
  -b "$repo_root/artifacts/packages" \
  -bc "$repo_root" \
  -pn "$package_name" \
  -pv "$package_version" \
  -ps "$package_supplier" \
  -nsb "https://local-sbom.invalid/$package_name"

echo "SBOM written under $repo_root/artifacts/packages/_manifest"
echo "(Build provenance attestation -- ADR-080's SLSA Level 2 target -- needs a real CI provider to sign against; not attempted by this local script.)"
