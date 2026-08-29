using Microsoft.EntityFrameworkCore;

namespace EventStore.Persistence;

// Direct request, this session: EventAppender/AccessLogAppender's own
// Serializable-isolation "read the tail, insert, compute chain" critical
// section is, by design (their own comments), exactly the shape that
// produces Postgres SQLSTATE 40001 ("could not serialize access due to
// read/write dependencies among transactions") whenever two appends
// genuinely overlap -- EventStore.Host.Postgres's EnableRetryOnFailure
// already retries these correctly (evidence-based, RetryOnFailurePostgresTests),
// but retry-after-abort is optimistic concurrency: every overlap still
// costs a full aborted transaction, a Postgres ERROR-level log line, and a
// retry/backoff cycle, observed as a genuinely alarming volume of errors
// under real concurrent load.
//
// TWO PRIOR ATTEMPTS, both wrong, kept documented here so neither is
// retried blind:
//
// Attempt 1 (ineffective): pg_advisory_xact_lock acquired as the first
// statement, but the transaction stayed SERIALIZABLE. Proven ineffective
// by RetryOnFailurePostgresTests.ConcurrentPublishesAgainstTheSameTail
// NeverTriggerAnEF40001Retry -- still ~97 real retries out of 30
// publishers. Root cause, understood only after this failed: a
// SERIALIZABLE transaction's snapshot is what Postgres's SSI conflict
// detection actually operates on; waiting inside one on a lock doesn't
// change or refresh that snapshot once acquired, so the lock ordered
// STATEMENT EXECUTION without changing what each transaction could see.
//
// Attempt 2 (unsafe): a SESSION-scoped pg_advisory_lock/pg_advisory_unlock
// pair, acquired before BeginTransactionAsync and released in a finally.
// Deadlocked the same regression test outright (>180s, had to be killed)
// -- suspected interaction between session-scoped locks and Npgsql
// connection pooling/EnableRetryOnFailure's own connection resets.
//
// THE ACTUAL FIX: keep the transaction-scoped lock from attempt 1 (no
// session/pooling lifecycle risk -- released automatically on commit OR
// rollback, every retry attempt starts clean), but drop the transaction's
// OWN isolation level to READ COMMITTED for Postgres specifically. Once
// the lock provides real mutual exclusion (only one appender is ever
// inside BEGIN..COMMIT for a given AppId's chain at a time), Serializable's
// extra guarantee is not just unnecessary, it's actively counterproductive:
// its snapshot is fixed regardless of the lock, which is exactly what made
// attempt 1 fail. Read Committed takes a fresh snapshot per statement, so
// the tail-read that runs immediately after acquiring the lock correctly
// sees the previous holder's already-committed row. Also closes a
// correctness gap a `SELECT ... FOR UPDATE` on the tail row itself would
// NOT have closed: pg_advisory_xact_lock's key is a fixed integer, not a
// data row, so it still serializes correctly even the very first insert
// into a brand-new, empty Events table (a FOR UPDATE lock has nothing to
// lock when no row exists yet -- a real, considered, and rejected
// alternative for exactly this reason).
//
// Confirmed by RetryOnFailurePostgresTests.
// ConcurrentPublishesAgainstTheSameTailNeverTriggerAnEF40001Retry: zero
// retries, repeated runs, plus ChainVerificationService confirming the
// resulting chain is genuinely valid (not just quiet).
//
// Scoped to Postgres only via db.Database.ProviderName -- SQLite has no
// genuine concurrent-writer scenario to guard against, and no other
// provider has ever produced this failure in practice; a SQL Server
// equivalent (sp_getapplock) is real, separate work if that provider is
// ever observed to need it, not built speculatively here. A provider-
// name check rather than a DI-resolved per-provider seam like
// IUniqueConstraintViolationDetector, deliberately: that interface exists
// because provider-specific EXCEPTION TYPES genuinely need compile-time
// polymorphism to catch correctly; this is one hardcoded, Postgres-only
// SQL statement (plus one isolation-level choice) with no other provider
// needing a different implementation.
public static class AppendSerializationLock
{
    private const string PostgresProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    // Two distinct, fixed keys -- EventAppender's Event Log chain and
    // AccessLogAppender's AccessLog chain are separate global chains
    // (ADR-045) with no correctness relationship to each other; sharing
    // one key would serialize them against each other for no reason.
    public const long EventLogTailLockKey = 0x4556454E544C4F47; // "EVENTLOG" (ASCII, folded to a long)
    public const long AccessLogTailLockKey = 0x4143434553534C47; // "ACCESSLG" (ASCII, folded to a long)

    public static bool IsPostgres(EventStoreContext db) => db.Database.ProviderName == PostgresProviderName;

    // Call as the FIRST statement inside the transaction (before any read
    // of the table this lock protects) -- the whole fix depends on that
    // ordering, see the class comment. No-op on every provider but
    // Postgres (caller decides the transaction's own isolation level;
    // this method only ever runs when that's already Read Committed).
    public static async Task AcquireAsync(EventStoreContext db, long lockKey, CancellationToken ct)
    {
        if (!IsPostgres(db)) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
    }
}
