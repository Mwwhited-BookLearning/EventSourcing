#!/usr/bin/env bash
# ADR-076 -- runs a bundle produced by generate-migration-bundle.sh against
# a real connection string, as the single deploy-time schema-apply step.
# The bundle itself is a self-contained executable -- no .NET SDK, no
# source code, no schema-altering permissions on the application's own
# runtime identity needed on the machine that runs it.
#
# Usage: scripts/apply-migration-bundle.sh <bundle-path> "<connection-string>"
set -euo pipefail

bundle="${1:?usage: apply-migration-bundle.sh <bundle-path> \"<connection-string>\"}"
connection_string="${2:?usage: apply-migration-bundle.sh <bundle-path> \"<connection-string>\"}"

chmod +x "$bundle" 2>/dev/null || true
"$bundle" --connection "$connection_string"
