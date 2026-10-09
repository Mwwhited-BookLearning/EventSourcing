# Wake-signal fault stops the whole host

## What was wrong

Under the concurrent AppHost load run (`src/EventStore.LoadTest`, 3000 publishes at concurrency 64), the Postgres host died: Postgres returned `too many clients already`, the LISTEN connection setup in `PostgresWorkerWakeSignal.WaitForWakeAsync` threw, the exception escaped the worker loop (workers call `WaitForWakeAsync` outside their per-tick try/catch), and the default `BackgroundServiceExceptionBehavior=StopHost` stopped the host. The failed `Lazy<Task<NpgsqlConnection>>` was also cached in the static `ListenConnections` map, so the fault would have repeated forever.

## How it was found

`aspire run` of the real AppHost plus the new load driver, then reading the Postgres host's service logs.

## Root cause

ADR-095 says the wake signal is an optimisation and the poll loop is the correctness backstop, but the implementation let a transient database fault propagate as an exception. Contributing cause: the AppHost Postgres container kept the default `max_connections=100` while every host/worker process pools up to 100 connections, and advisory-lock waiters hold theirs.

## Resolution

- `PostgresWorkerWakeSignal` and `SqlServerWorkerWakeSignal`: `WaitForWakeAsync` now catches non-cancellation faults, logs a warning, evicts the cached Postgres listen connection, and falls back to a short delay (the poll loop continues to drive the worker).
- AppHost: Postgres container started with `-c max_connections=300`.
- Re-run: 3000 publishes, all 202, `/events/verify` clean, 200/200 sampled entities routed on the Postgres host.
