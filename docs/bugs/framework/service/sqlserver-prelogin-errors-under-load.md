# SQL Server host logs pre-login connection errors during the concurrent load run

**Status: closed 2026-10-09, not reproducible on a clean run (likely restart churn).**

## What was wrong

During the first concurrent AppHost load run, the SQL Server host logged 135 `SqlException` pre-login handshake errors (also seen from `SqlServerHealthCheck` in the Playwright E2E output). Publishes still returned 202 and the chain verified, so the effect is unknown: transient connect failures that retry, or lost work.

## How it was found

`aspire logs eventstore-sqlserver` during the load run; counted, not examined.

## Root cause

Unknown. Suspects: the SQL Server container is slow to accept logins while startup/migrations and the load overlap, or the connection pool is exhausted under 64 concurrent publishers plus the workers' dedicated Service Broker `WAITFOR` connections.

## Re-run

After a full clean restart of the AppHost (stale containers and networks removed, see `aspire-restart-leaves-stale-containers-and-dead-ports.md`), the same load (3000 publishes, concurrency 64, six types, SQL Server host) produced 202 x 3000, a clean chain verify, and zero pre-login errors in `aspire logs eventstore-sqlserver` (the only SQL errors were the already-handled `PK_EventTypeDefinitions` bootstrap-race retries). The original 135 were seen during repeated AppHost restarts with a half-started container, so they were most likely connection attempts against a database that was not yet accepting logins. Reopen with the first error's full message if it recurs on a clean start.
