# SQL Server host logs pre-login connection errors during the concurrent load run

**Status: open, not root-caused.**

## What was wrong

During the first concurrent AppHost load run, the SQL Server host logged 135 `SqlException` pre-login handshake errors (also seen from `SqlServerHealthCheck` in the Playwright E2E output). Publishes still returned 202 and the chain verified, so the effect is unknown: transient connect failures that retry, or lost work.

## How it was found

`aspire logs eventstore-sqlserver` during the load run; counted, not examined.

## Root cause

Unknown. Suspects: the SQL Server container is slow to accept logins while startup/migrations and the load overlap, or the connection pool is exhausted under 64 concurrent publishers plus the workers' dedicated Service Broker `WAITFOR` connections.

## Next step

Re-run `EventStore.LoadTest` against :5002 with a fresh database, capture the first error's full message (timeout vs. refused vs. pool), and compare against the pool size and the number of long-lived `WAITFOR` sessions.
