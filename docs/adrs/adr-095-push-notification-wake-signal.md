[← ADR index](../07-adrs.md)

# ADR-095: A push-notification wake-up layer on top of every background worker's own poll loop, never a replacement for it

Status: Accepted

Context: `docs/10-open-questions.md` tracked a genuine, unresolved fork:
every background worker in this design (`RouterWorker`, `DerivationWorker`,
`WebhookOutboxPump`, `PeerSyncWorker`, `ChannelDerivationWorker`,
`ExpectedResponseWatcher`) advances via a fixed-interval poll loop against
the database, never a push notification. Direct decision, this session:
add a "wake sooner" layer, per provider, proven end-to-end on
`RouterWorker` first — the most central worker — before extending to the
other five, a deliberate staged rollout rather than a single, larger,
less-verified pass across all six at once.

**Genuinely a multi-way fork with no clearly superior default across this
design's three providers (`ADR-001`)**, weighed in full by the open
question this ADR resolves: Postgres `LISTEN`/`NOTIFY` is real and native
but fire-and-forget (no durable queue — a disconnected listener misses a
`NOTIFY` outright); SQL Server Service Broker is a genuine durable,
transactional queue whose distinguishing feature is *activation* (internal
or external), not just durability; SQLite has no cross-process
notification primitive at all — it isn't a client-server database, and its
own `sqlite3_update_hook` is process-local only. A dedicated external
broker (RabbitMQ) was considered and rejected: a new infrastructure
dependency this design doesn't have anywhere today, against `ADR-041`'s
own explicit-composition/no-third-party-magic framing, for a need every
provider's own real, already-present mechanism already covers.

Decision:
- **The poll loop stays the sole correctness guarantee, on every provider,
  always.** This is not a replacement mechanism — a missed, dropped, or
  never-delivered signal (a disconnected Postgres listener; a Service
  Broker message no one happened to be listening for; SQLite's own brief
  restart window) simply means a worker waits its full, already-safe poll
  interval, exactly the behavior it had before this ADR. The well-
  established real-world shape for this is "notify-to-wake,
  poll-to-confirm" — `NOTIFY` (or its per-provider equivalent) shortens the
  sleep; the poll-based read is what actually decides there's real work.
- **One shared abstraction, `IWorkerWakeSignal` (`EventStore.
  WorkerWakeSignal`)**: `NotifyAsync(topic, ct)` (called by a publisher
  right after a durable write actually succeeds, never before) and
  `WaitForWakeAsync(topic, maxWait, ct)` (called by a worker in place of
  an unconditional `Task.Delay(pollInterval)` between ticks that found
  nothing). `RouterWorker.ExecuteAsync` calls the latter; `PublishService.
  PublishAsync` calls the former immediately after `EventAppender.
  AppendAsync` returns, on topic `"router"`.
- **Postgres: `LISTEN`/`NOTIFY`** (`PostgresWorkerWakeSignal`). `NotifyAsync`
  issues `SELECT pg_notify(@topic, '')` on `EventStoreContext`'s own
  connection/ambient transaction — if the transaction that just wrote the
  event already committed (it has, by the time this runs), Postgres
  delivers immediately; if it somehow hadn't, Postgres itself defers
  visibility to listeners until commit either way, which is exactly the
  "never signal before the write is durable" contract this interface
  requires. `WaitForWakeAsync` holds one dedicated, long-lived `LISTEN`
  connection per topic for this process's lifetime (`LISTEN` is
  session-scoped; a fresh connection per call would re-subscribe from
  scratch every tick and could miss a `NOTIFY` fired in the gap).
- **SQL Server: Service Broker** (`SqlServerWorkerWakeSignal`) — a real
  message type/contract/queue/service, created by migration
  (`AddWorkerWakeSignal`), `ENABLE_BROKER` on the hosting database.
  `NotifyAsync` opens a dialog, sends one message, ends the conversation;
  `WaitForWakeAsync` uses `WAITFOR (RECEIVE ...), TIMEOUT n` directly
  against the queue — the standard client-side Service Broker consumption
  pattern, not internal/external activation. **Deliberately without
  activation, a direct decision distinguishing two genuinely different
  Service Broker use cases**: internal activation (SQL Server auto-invoking
  a T-SQL stored procedure) would race this worker's own already-listening
  C# `WAITFOR`/`RECEIVE` call for the same messages — a real correctness
  hazard, not a missing feature, since a live .NET worker is *always* the
  active listener here. External activation (waking a genuinely separate,
  possibly non-.NET process that ISN'T continuously polling) is Service
  Broker's own real, different target for that mechanism — named as a real
  future extension point for a genuinely external consumer, not built here
  since every consumer in this build is already a live, running worker.
- **SQLite: an in-process signal, backed by a durable marker row**
  (`SqliteWorkerWakeSignal`, `WakeSignal` entity — `Topic` PK,
  `LastSignaledAt`). SQLite has no cross-process notification primitive at
  all, but every `Host.Sqlite` deployment already runs every background
  worker in the same process as the Inbox that publishes, so an in-process
  `Channel<T>` (one per topic, shared via static state within that one
  process) is the entire real mechanism the common case needs. The durable
  `WakeSignal` row exists for a narrower, real edge case: a worker that
  hasn't yet observed any wake since its own process started can still
  notice a signal that already happened (e.g. a publish landing in the gap
  between migration/startup and this worker's first `WaitForWakeAsync`
  call) via the row's own `LastSignaledAt`, rather than waiting out a full
  poll interval on its very first tick.
- **`WakeSignal` migrated uniformly across all three providers**, per
  `CLAUDE.md`'s own "avoid a second wave of migrations" precedent, even
  though only the SQLite implementation actually depends on it for
  correctness — Postgres/SQL Server's own native transports already
  durably queue/notify without it.

Consequences:
- **Resolves `docs/10-open-questions.md`'s row** on this exact fork.
- **Scoped to `RouterWorker` only, a deliberate staged rollout, not the
  finished job.** `DerivationWorker`, `WebhookOutboxPump`,
  `PeerSyncWorker`, `ChannelDerivationWorker`, and `ExpectedResponseWatcher`
  still poll on a fixed interval alone — extending each to call
  `WaitForWakeAsync` (and wiring the matching `NotifyAsync` call at
  whichever write path feeds it) is mechanical once a topic name is picked
  per worker, not a new design. Tracked as real, named follow-up work, not
  swept under this ADR's own exit criteria.
- **The SQL Server Service Broker queue/service is scoped to ONE topic
  ("router") for this pass** — one shared queue, no per-topic message
  routing or `message_body` filtering. Extending to more workers needs
  either per-topic queues or a topic check on `RECEIVE`, neither built
  here since there was only one topic to receive.
- **Two real bugs found only by actually running this, not by design
  review alone**: (1) `SqliteWorkerWakeSignal`'s static in-process state,
  keyed by topic alone, let unrelated `WebApplicationFactory`-hosted test
  processes (this suite's own MSTest 32-way parallelism runs many
  independent Hosts, each its own SQLite file, in one shared test process)
  cross-talk through the same `Channel` — fixed by keying on `(connection
  string, topic)` instead, isolating unrelated Hosts while still sharing
  state correctly within one real deployment's own single connection
  string. (2) `ALTER DATABASE ... SET ENABLE_BROKER` cannot target the
  `master` system database at all ("Option 'ENABLE_BROKER' cannot be set
  in database 'master'"), which broke every pre-existing `*SqlServerTests.
  cs` file in this suite (all of which migrate against Testcontainers' own
  default `master` connection, the established convention long before this
  ADR) — fixed by wrapping the `ENABLE_BROKER` attempt in `TRY`/`CATCH` and
  gating the `CREATE MESSAGE TYPE`/`CONTRACT`/`QUEUE`/`SERVICE` statements
  on `is_broker_enabled` actually being true, so a `master`-connected
  migration now just skips creating Service Broker objects entirely rather
  than failing. `WorkerWakeSignalSqlServerTests.cs` uses a real, named,
  non-system database specifically so Broker really is active where the
  mechanism is actually exercised end to end.
- Verified end-to-end against real infrastructure, not mocked: Postgres
  and SQL Server via Testcontainers (`WorkerWakeSignalPostgresTests.cs`,
  `WorkerWakeSignalSqlServerTests.cs`), SQLite directly
  (`WorkerWakeSignalSqliteTests.cs`) — each proving both halves: a
  `NotifyAsync` during an active wait wakes it well before its own
  timeout, and a wait with no signal at all still runs out its full
  timeout as the correctness backstop.
