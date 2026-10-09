[← Bugs index](../../../changes/2026-10-09.md)

# SQL Server: concurrent publishes deadlock and surface as HTTP 500

**Scope**: `framework` · **Tier**: `database`

## What was wrong

Twenty concurrent `POST /publish/{eventType}` calls against
`EventStore.Host.SqlServer` returned 500 for 18 of 20 (only 2 accepted).
The server-side exception was `SqlException 1205: Transaction was
deadlocked on lock resources with another process and has been chosen as
the deadlock victim`, wrapped in an `InvalidOperationException` suggesting
`EnableRetryOnFailure`. The same test passes on Postgres.

## How and where it was found

The new provider-parity e2e suite (`ProviderE2EScenarios.
HashChainVerifiesCleanAfterConcurrentPublishes`, run once per provider via
`ProviderE2EPostgresTests` / `ProviderE2ESqlServerTests`). No existing
SQL Server test publishes concurrently, so the Serializable read-tail /
insert / chain critical section was never exercised under overlap there.

## Root cause

`EventAppender` / `AccessLogAppender` run the append inside a Serializable
transaction. On SQL Server, two overlapping appenders both take shared
range locks on the tail read and then each try to convert to an exclusive
lock to insert: a classic conversion deadlock. `Host.SqlServer` also
registers `UseSqlServer` without `EnableRetryOnFailure`, so the victim is
never retried. `AppendSerializationLock`'s own header anticipated this
("a SQL Server equivalent (sp_getapplock) is real, separate work if that
provider is ever observed to need it").

## Resolution

Fixed 2026-10-09. `AppendSerializationLock.AcquireAsync` now takes a
transaction-owned `sp_getapplock` (Exclusive, infinite wait) on SQL Server
as the first statement of the append, and `UsesTailLock` drops the
transaction to Read Committed for SQL Server as for Postgres (the lock, not
Serializable, provides the exclusion). `Host.SqlServer` also gained
`EnableRetryOnFailure()` as a backstop. Verified by the provider e2e suite
and the full 264-test integration run.
