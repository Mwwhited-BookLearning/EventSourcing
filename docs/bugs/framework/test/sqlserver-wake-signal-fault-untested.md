# SQL Server wake signal's "never throws" rule has no fault test

**Status: resolved.**

## What was wrong

`SqlServerWorkerWakeSignal.WaitForWakeAsync` was changed to swallow database faults (ADR-095, `wake-signal-fault-stops-host.md`), but only the Postgres implementation has a regression test (`WorkerWakeSignalPostgresTests.WaitForWakeNeverThrowsWhenTheDatabaseIsUnreachable`). A regression on SQL Server would stop the host again and nothing would catch it.

## Resolution

Added `WorkerWakeSignalSqlServerTests.WaitForWakeNeverThrowsWhenTheDatabaseIsUnreachable` (unreachable server, passes).
