# In-database native predicate evaluator spikes

`ADR-098` designed a pluggable seam (`IEncryptedPredicateEvaluator`) for
running the exact-match decrypt-and-compare step of an encrypted range
query *inside* the database engine, as an alternative to the default,
already-adopted `AppTierEncryptedPredicateEvaluator` (which decrypts
only the already-narrowed candidate set, fetched to the application
tier). This folder holds every native-evaluator implementation actually
built and benchmarked while exploring that seam — moved here from
`src/`/`tests/`/`scripts/sql-clr/`, 2026-09-04, direct request ("I don't
think the performance of this feature is worth the effort... left
behind as a POC"), after real, measured benchmarking found no clear,
consistent win over the app-tier default once real SQL Server/PostgreSQL
execution-model trade-offs are accounted for.

**None of these is wired into `EventStore.slnx`** — matching every other
spike folder's own convention, this is real working code (not
pseudocode), run directly with `dotnet build`/`dotnet test`/`docker
build`, but not a production dependency. The app-tier default remains
the only evaluator actually adopted.

## At a glance

| Spike | Language/tooling | What it does | Real, measured result |
|---|---|---|---|
| [`SqlServerSqlClrSpike/`](SqlServerSqlClrSpike/) + [`SqlServerSqlClrSpike.Tests/`](SqlServerSqlClrSpike.Tests/) | C# (net48 SQLCLR), zero NuGet packages | A from-scratch AES-256-GCM (NIST SP 800-38D) implementation (`PureNet48AesGcm.cs`) using only `System.Security.Cryptography.Aes`'s ECB primitive — built to avoid two real, independently-discovered SQL Server deployment blockers a `Microsoft.Bcl.Cryptography`-based version hit (a CLR-verifier failure under `SAFE`, then a `HostProtectionException` from `Aes.Create()`'s default CAPI provider). Both a per-row scalar function (`DecryptAndCompare`) and a batch table-valued function (`DecryptAndCompareBatchBench`) are implemented. | Scalar: correct, but **slower than the app-tier default at 50,000 rows** (~720ms vs ~102ms) — real per-row SQLOS↔CLR call overhead, compounded by `AesManaged` having no hardware AES-NI acceleration. Crosses over to *faster* at 1,000,000 rows (~780ms vs ~1,640ms) only because SQL Server parallelizes the scalar-function scan across cores. The batch TVF (avoiding per-row overhead entirely) is faster than the scalar function at 50,000 rows (~418ms) — confirming batching genuinely helps holding parallelism equal — but SQL Server forces CLR table-valued functions with real data access to run *serially*, so at 1,000,000 rows it loses the parallelism the scalar approach gained, and comes in far slower (~9,300ms). No single implementation wins outright across scales. |
| [`PostgresCExtensionSpike/`](PostgresCExtensionSpike/) | C (PGXS) | A genuinely native PostgreSQL extension (`decrypt_and_compare_c`), linking OpenSSL's EVP API directly for real, hardware-accelerated AES-256-GCM. | The clear winner among everything tried: ~33ms/~238ms (50K/1M rows) — 2.5–13× faster than the app-tier default, gap widening with scale. Never wired into a real `IEncryptedPredicateEvaluator` C# implementation. |
| [`PostgresRustExtensionSpike/`](PostgresRustExtensionSpike/) | Rust (`pgrx` 0.19.2, `aes-gcm` crate) | An incomplete attempt at a `pgrx`-based (not `plrust` — that trusted-language wrapper is pinned to an old `pgrx` capped at PostgreSQL 16) native Rust extension. Source only (`lib.rs`/`Cargo.toml`) — **never built or benchmarked**; work stopped once the C extension's own real numbers, plus a direct SQLCLR request, took priority, and the final "not worth the effort" verdict landed before returning to it. | Unknown — not measured. |
| [`deploy-sql-server-encrypted-predicate-function.sql`](deploy-sql-server-encrypted-predicate-function.sql), [`deploy-postgres-encrypted-predicate-function.sql`](deploy-postgres-encrypted-predicate-function.sql) | T-SQL / PL/pgSQL | Real deployment scripts for the SQL Server and PostgreSQL (`plpython3u`) native evaluators. | `plpython3u`: correct, but **slower than the app-tier default at every scale tested** (~175ms/~3,133ms vs ~82ms/~1,239ms) — interpreter/SPI-marshalling overhead outweighs its bandwidth savings. |

## The actual verdict

**Not adopted.** Every native-evaluator path tried either lost to the
already-built, already-simple app-tier default at realistic scale, won
only inconsistently across scale (SQL Server's own scalar/batch
crossover), or was never finished measuring (Rust). The one clear,
consistent winner — the PostgreSQL C extension — was still never wired
into production `IEncryptedPredicateEvaluator` selection, and adopting
a C extension carries its own real, ongoing cost (a second language/
toolchain, a build step outside the normal `dotnet build`/EF Core
migration pipeline) this session's own investigation didn't weigh
against the app-tier default's "already works, zero extra
infrastructure" baseline.

**A possible future direction, named honestly rather than pursued**:
`unsafe` pointer arithmetic (avoiding bounds-checked array access in
`PureNet48AesGcm`'s hot `GHASH` loop) could plausibly close some of the
SQLCLR scalar-function gap. It was not attempted — `unsafe` C# compiles
to unverifiable IL, which would very likely need `PERMISSION_SET =
UNSAFE` to load at all, reopening the exact SQL Server Linux
(`Testcontainers`, this project's own real infrastructure) deployability
question `PureNet48AesGcm.cs` was built specifically to close. Worth
revisiting only alongside a real answer to that question, not as a pure
performance tweak.

Full investigation, every real number, and both real deployment bugs
found along the way: `docs/08-build-plan.md`'s "In-Database Native
Predicate Evaluator Seam" item and `docs/adrs/adr-098-in-database-
predicate-evaluator-seam.md`'s own additive notes, both dated
2026-09-04.
