#!/usr/bin/env bash
# ADR-076 -- EF Core Migration Bundles applied as a single deploy-time
# step, before any EventStore.Host.<Provider> replica starts (no replica
# ever calls Database.Migrate()/MigrateAsync() at startup itself, since
# two replicas starting concurrently against a fresh database is a known
# race). This is the local/POC equivalent of the "explicit migration-
# bundle-generation-and-apply step sequenced before any Host container
# starts" ADR-076's own Consequences names as not-yet-built pipeline
# wiring -- wiring that into docker-compose.yml/EventStore.AppHost itself
# remains flagged, not done this pass.
#
# Usage: scripts/generate-migration-bundle.sh <sqlite|postgres|sqlserver> [output-path]
set -euo pipefail

provider="${1:?usage: generate-migration-bundle.sh <sqlite|postgres|sqlserver> [output-path]}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

case "$provider" in
  sqlite)    migrations_project="EventStore.Persistence.Migrations.Sqlite";    host_project="EventStore.Host.Sqlite" ;;
  postgres)  migrations_project="EventStore.Persistence.Migrations.Postgres";  host_project="EventStore.Host.Postgres" ;;
  sqlserver) migrations_project="EventStore.Persistence.Migrations.SqlServer"; host_project="EventStore.Host.SqlServer" ;;
  *) echo "unknown provider '$provider' -- expected sqlite, postgres, or sqlserver" >&2; exit 1 ;;
esac

output="${2:-$repo_root/scripts/bundles/$provider/efbundle}"
mkdir -p "$(dirname "$output")"

dotnet ef migrations bundle \
  --project "$repo_root/src/$migrations_project" \
  --startup-project "$repo_root/src/$host_project" \
  --output "$output" \
  --force

echo "Bundle written to $output"
