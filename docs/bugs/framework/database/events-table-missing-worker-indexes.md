# Events table has no indexes for the background workers' queries

## What was wrong

`Events` was indexed only on `SequenceNumber` (PK), `EventId` and `EntityId`. Every background worker query filtered on something else, so each one scanned the whole log: `RouterWorker`'s "received, past the cursor" page (`Status`), `UpcastMaterializer.ReconcileBacklogAsync` (`AppId`/`EventType`, and a `MaterializationOfEventId IN (...)` anti-join), and `ExpectedResponseWatcher` (`EventType` + `RespondsToEventId`). On a few hundred thousand rows each scan cost seconds of CPU, and the reconcile ran on every 200 ms Router tick.

## How it was found

The concurrent AppHost load run on SQL Server: the Router routed ~9 events/s (Postgres ~230/s). Sampling `sys.dm_exec_requests` showed Router sessions running, not blocked, with 100+ s of accumulated CPU on the `@activeType_*` reconcile query and the `@responseEventType` query. Query shape alone did not show a scan; the missing indexes did (`sys.indexes` for `Events`).

## Root cause

The ADR that added each worker query never added the supporting index (the "ADR owns the field and its index" rule applied to columns, not to the access paths). Providers with cheaper scans (Postgres) hid it.

## Resolution

- Migration `AddEventsWorkerIndexes` on all three providers: `(Status, SequenceNumber)`, `(MaterializationOfEventId)`, `(AppId, EventType, SequenceNumber)`, `(RespondsToEventId)`.
- `UpcastMaterializer.ReconcileBacklogAsync` now does the "already materialized" check as a `NOT EXISTS` anti-join in the database, paged by `SequenceNumber` (200/page, no tracking), instead of loading every older-version event and a giant `IN` list.
- `RouterWorker` runs the reconcile at most every 30 s (it is a catch-up scan with "no pacing guarantee" per ADR-027), not on every tick.

Result: SQL Server Router ~9/s to ~100/s sustained under concurrent publishing; a 3000-event backlog drains in ~40 s once publishing stops.

Remaining: SQL Server stays slower than Postgres (Service Broker dialogs flush the log on every wake-signal send); see `TODO.md`.
