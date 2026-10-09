# Router tick loads every received event into one unit of work

## What was wrong

`RouterWorker` loaded every `received` event into one change tracker and saved once at the end. After a burst (17,491 events on the SQL Server host) one tick took minutes of change-tracking work, committed nothing until the end, held the leader lease far past its renewal point, and one bad event discarded the whole tick. The backlog never drained.

## How it was found

The concurrent AppHost load run; the SQL Server host's router sat on a growing `received` backlog.

## Root cause

No upper bound on a tick and no incremental commit.

## Resolution

`RouterWorker.RunTickAsync` pages by `SequenceNumber` cursor in batches of `BatchSize` (500), commits and clears the change tracker per page, and stops once `BatchSize` events have been applied. Paging by cursor (not a plain `Take`) keeps events deferred by ADR-038's rollback gate, which stay `received` forever, from starving later events. The worker loops straight into the next tick while batches come back full. Regression tests: `RouterBatchingSqliteTests`. `RunOnceAsync` keeps its signature (returns events processed).

Follow-up found and fixed separately: SQL Server routed only ~9 events/s because `Events` lacked worker indexes, see `docs/bugs/framework/database/events-table-missing-worker-indexes.md`.
