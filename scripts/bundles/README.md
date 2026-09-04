# `scripts/bundles/`

Build-once, deploy-time EF Core Migration Bundle binaries (`ADR-076`),
one per provider (`sqlite/`, `postgres/`, `sqlserver/`), each an
`efbundle` executable produced by `scripts/generate-migration-bundle.sh
<provider>`. **Gitignored** — nothing under this directory is tracked in
version control, so nothing here is ever caught by review, CI, or a
stale-file diff.

**These are build-once artifacts, not something that updates itself.**
A bundle only reflects the migrations that existed in
`src/EventStore.Persistence.Migrations.<Provider>/Migrations/` at the
moment `generate-migration-bundle.sh` last ran. Add a new EF Core
migration and forget to regenerate the matching bundle, and
`apply-migration-bundle.sh`/`docker-compose.yml`'s `migrate` service (or
any `EventStore.Host.<Provider>` process started against a database that
bundle only partially migrates) fails in a confusing, indirect way — a
missing-column crash partway through startup, not an obvious "bundle is
stale" error.

Found for real, `2026-09-04`: a local Sqlite bundle here was 17 days
behind the real migrations directory (missing `ADR-094`'s
`ExpectedResponse` column) while proving the SDK-codegen story end to
end (`docs/changes/2026-09-04.md`) — discovered only by a real Host
process crashing on startup, not by anything catching it beforehand.

**Regenerate whenever a new migration lands**, before relying on a
bundle here:

```sh
scripts/generate-migration-bundle.sh <sqlite|postgres|sqlserver>
```
