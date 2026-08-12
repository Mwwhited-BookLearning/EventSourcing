#!/usr/bin/env bash
# ADR-076 -- EF Core Migration Bundles applied as a single deploy-time
# step, before any EventStore.Host.<Provider> replica starts (no replica
# ever calls Database.Migrate()/MigrateAsync() at startup itself, since
# two replicas starting concurrently against a fresh database is a known
# race). Run this once (a deploy pipeline's own build step, ahead of
# `docker compose up`, not something docker-compose.yml runs itself --
# generating a bundle needs the .NET SDK, which the Postgres provider's own
# `migrate` service there deliberately doesn't carry) before
# apply-migration-bundle.sh/docker-compose.yml's own `migrate` service
# consumes the bundle this writes to scripts/bundles/<provider>/efbundle.
# (EventStore.AppHost, the separate local-dev orchestration story, uses a
# different mechanism entirely -- its own EventStore.Migrator project calls
# Database.MigrateAsync() directly, no bundle involved.)
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

# --self-contained/--target-runtime linux-x64: docker-compose.yml's own
# `migrate` service runs this bundle inside a Linux container (matching
# every Host image it builds), not necessarily on the machine that generated
# it -- a framework-dependent or non-Linux bundle would fail to start there.
dotnet ef migrations bundle \
  --project "$repo_root/src/$migrations_project" \
  --startup-project "$repo_root/src/$host_project" \
  --output "$output" \
  --self-contained \
  --target-runtime linux-x64 \
  --force

echo "Bundle written to $output"
