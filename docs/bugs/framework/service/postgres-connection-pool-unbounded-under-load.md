# Postgres hosts have no connection pool cap sized against the server's max_connections

**Status: resolved 2026-10-09.**

## What was wrong

The load run exhausted Postgres `max_connections` (`too many clients`). The immediate fix raised the AppHost container to `max_connections=300` and made the wake signal non-throwing, but each host's Npgsql pool still had the default maximum (100) plus one dedicated LISTEN connection per topic, and several hosts share one server. Nothing tied the pool size to the server limit.

## Resolution

`EventStore.Host.Postgres/Program.cs` rewrites `ConnectionStrings:Postgres` at startup to set `Maximum Pool Size=40` unless the configured string already specifies one. Budget: hosts x 40 + LISTEN connections (one per wake topic) must stay under `max_connections` (300 in the AppHost, which runs one Postgres host plus simulators/seeds). Raise `max_connections` with any added host.
