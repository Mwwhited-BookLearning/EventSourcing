[← Bugs index](../../../changes/2026-08-29.md)

# Postgres: routine `40001` "could not serialize access" noise under real concurrent load

**Scope**: `framework` · **Tier**: `database`

## What was wrong

Running a real `AppHost` against a real Postgres container produced a
steady stream of visible errors: `Npgsql.PostgresException: '40001:
could not serialize access due to read/write dependencies among
transactions'`. Direct report: "I'm getting a ton of errors running the
application... it should not error this much. it should be much more
graceful for retrys and so on."

## How and where it was found

`EventStore.Host.Postgres`'s `EnableRetryOnFailure(maxRetryCount: 20,
maxRetryDelay: 2s, errorCodesToAdd: ["3D000", "40001"])` was already in
place from earlier hardening, and in principle should have made 40001
invisible to any caller. Confirmed this directly rather than trusting
the config alone: started a real `AppHost` against its real Postgres
container, then read the **container's own log** (`docker logs`), not
just the .NET side — this showed real, recurring `ERROR: could not
serialize access due to read/write dependencies among transactions`
lines under nothing more than the two proving-ground Simulators' own
background ticks. `RetryOnFailurePostgresTests` (the existing suite)
confirmed every one of these was already being retried away correctly
with no chain corruption — so the retries were correct, but the
*frequency* of the underlying conflict was the actual problem.

## Root cause

`EventAppender.AppendAsync`/`AccessLogAppender.AppendAsync`'s
Serializable-isolation critical section (`ADR-019`/`ADR-033`/`ADR-045`)
reads the current chain tail, inserts a new row, then computes
`ChainHash`/`LogicalClock` off what was just read — by construction,
exactly the read/write pattern Postgres's SSI (Serializable Snapshot
Isolation) flags whenever two appenders genuinely overlap. The existing
retry-on-failure config treats the resulting abort as an expected,
retryable outcome — correct, but optimistic: every overlap still costs
one full aborted transaction and one ERROR-level log line before the
retry succeeds.

## Resolution attempts (two failed, kept here so neither is retried blind)

1. **`pg_advisory_xact_lock` acquired after `BeginTransactionAsync
   (Serializable)`.** Ineffective — proven by the regression test below
   (30 concurrent publishers still produced ~97 real retries). A
   Serializable transaction's snapshot is what Postgres's SSI conflict
   detection actually operates on; a lock acquired *inside* an already-
   open transaction orders statement execution but doesn't refresh that
   snapshot, so the lock changed nothing about what each transaction
   could see.
2. **A session-scoped `pg_advisory_lock`/`pg_advisory_unlock` pair**,
   acquired before `BeginTransactionAsync` and released in a `finally`.
   Unsafe — deadlocked the same regression test outright (had to be
   killed after >180s). Suspected interaction between session-scoped
   locks and Npgsql connection pooling/`EnableRetryOnFailure`'s own
   connection resets.

## Actual fix

`src/EventStore.Persistence/AppendSerializationLock.cs`: keeps the
transaction-scoped `pg_advisory_xact_lock` from attempt 1 (no session/
pooling lifecycle risk — released automatically on commit *or*
rollback), but drops the transaction's own isolation level to **Read
Committed** for Postgres specifically, instead of Serializable. Once the
lock provides real mutual exclusion (only one appender is ever inside
`BEGIN..COMMIT` for a given chain at a time), Serializable's extra
guarantee is not just unnecessary, it actively breaks the fix — its
snapshot is fixed regardless of the lock, which is exactly what made
attempt 1 fail. Read Committed takes a fresh snapshot per statement, so
the tail-read immediately after acquiring the lock correctly sees the
previous holder's already-committed row.

This also closes a correctness gap a `SELECT ... FOR UPDATE` on the tail
row itself (a real, considered alternative) would **not** have closed:
`pg_advisory_xact_lock`'s key is a fixed integer, not a data row, so it
still serializes correctly even the very first insert into a brand-new,
empty `Events` table — a `FOR UPDATE` lock has nothing to lock when no
row exists yet.

- `src/EventStore.Persistence/EventAppender.cs`, `AccessLogAppender.cs`:
  Postgres uses `IsolationLevel.ReadCommitted` + one
  `AppendSerializationLock.AcquireAsync` call as the first statement in
  the transaction; SQLite/SQL Server are completely unchanged
  (`IsolationLevel.Serializable`, no lock) — neither has ever exhibited
  this failure.
- `EnableRetryOnFailure`'s existing 3D000/40001 config in
  `EventStore.Host.Postgres/Program.cs` left unchanged, as
  defense-in-depth for any other conflict source.
- **Regression tests** (`RetryOnFailurePostgresTests.cs`):
  - `ConcurrentPublishesAgainstTheSameTailNeverTriggerAnEF40001Retry`
    (`[TestProperty("BugReport", "docs/bugs/framework/database/postgres-
    routine-40001-serialization-noise.md")]`) — 30 concurrent publishers
    against an established tail, counting EF Core's own
    `CoreEventId.ExecutionStrategyRetrying` diagnostic event and
    asserting zero, plus a `ChainVerificationService` check that the
    resulting chain is genuinely valid. Confirmed red against both
    failed attempts above (97 retries against attempt 1; a hang against
    attempt 2) and green against the actual fix, repeated 3 times with
    no flakiness.
  - `ConcurrentPublishesAgainstFreshlyRegisteredEventTypeWithNoPriorEventsNeverCorruptTheChain`
    — 20 concurrent publishers as the very first events ever written
    (no seed publish first), specifically proving the genesis-race gap a
    `FOR UPDATE`-based fix would not have covered is actually closed.
  - Full existing suite (`RetryOnFailurePostgresTests`, all 7; full
    `EventStore.IntegrationTests`, all 244) still green — correctness
    unchanged for every other scenario.
- **Live verification**: ran a real `AppHost` against a real Postgres
  container for several minutes of genuine simulator traffic and read
  the container's own logs directly (the same technique that found the
  bug) — zero `40001`/serialization errors observed, versus the routine
  recurrence seen before the fix.

No ADR update: an internal concurrency-control implementation detail of
an already-decided append mechanism, not a new architectural decision or
a change to any persisted shape/contract.
