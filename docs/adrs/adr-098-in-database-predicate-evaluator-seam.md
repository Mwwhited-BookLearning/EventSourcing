[← ADR index](../07-adrs.md)

# ADR-098: Pluggable in-database native predicate evaluator seam (designed, not yet built)

Status: Accepted — design only; `08-build-plan.md` sequences the build later

Context: `ADR-096`'s bucketed range index still needs one exact-match
decrypt-and-compare step over a narrowed candidate set once bucket
lookup identifies which rows might match. Direct request: run queries
inside the database as much as possible, and not require downloading
large amounts of ciphertext into the backend service's own memory to
decrypt and filter. This ADR designs a seam for doing that exact-match
step natively inside the database engine — SQLCLR (SQL Server), a native
function (PostgreSQL), or an app-registered function (SQLite) — rather
than always falling back to the application tier. Per direct decision,
this pass designs the seam only; concrete per-provider evaluators are
separate, later `08-build-plan.md` items.

Decision:
- **`IEncryptedPredicateEvaluator`**, the same Strategy/keyed-DI shape as
  `IErasureKeyStore`/`IMaskingStrategy` (`ADR-057`/`ADR-009`,
  `docs/patterns/strategy-pattern-extensible-masking.md`):
  ```csharp
  public interface IEncryptedPredicateEvaluator
  {
      Task<IReadOnlyList<long>> EvaluateAsync(
          IReadOnlyList<long> candidateSequenceNumbers,
          string fieldJsonPath, FilterableFieldType dataType,
          string comparisonOperator, string comparisonValue,
          CancellationToken ct);
  }
  ```
  Takes the **already-narrowed** candidate set `ADR-096`'s bucket lookup
  (or `ADR-097`'s ORE ciphertext comparison, for the rare case a
  secondary exact check is still wanted) produces, and returns which of
  those rows actually satisfy the exact comparison. **Never called
  against a full, un-narrowed table** — that boundary is enforced by
  `GraphQlFilterPredicateBuilder`'s own query routing (`ADR-096`), not by
  this interface itself, so this stays a narrow, single-purpose seam
  rather than a general query engine.
- **Default implementation**: `AppTierEncryptedPredicateEvaluator` —
  fetches only the narrowed candidate rows' ciphertext, decrypts via the
  existing `EnvelopeAesGcm`/`IErasureKeyStore` path (`ADR-057`), and
  compares in application memory. This is the only implementation this
  pass builds; it directly satisfies "don't pull large amounts of data
  into memory" simply by construction, since the candidate set is
  already small after bucket narrowing — no code changes needed to
  `EventStore.Erasure` beyond registering this default.
- **Per-provider native alternative, documented here, each checked
  against the real mechanism rather than assumed feasible**:
  - **SQL Server — SQLCLR scalar function.** Real, mainstream, and the
    natural fit — but requires assembly signing under `clr strict
    security` (the default posture since SQL Server 2017 CU12/2019), and
    many organizations disable CLR integration entirely by policy. Named
    as a real deployment constraint, not assumed always available.
  - **PostgreSQL — a small custom function, not bare `pgcrypto`.**
    `pgcrypto`'s own `pgp_sym_encrypt`/`pgp_sym_decrypt` speak OpenPGP CFB
    framing, not raw AES-GCM — they cannot read `EnvelopeAesGcm`'s
    `nonce || tag || ciphertext` wire format directly. A real
    implementation needs either a PL/pgSQL function built from
    `pgcrypto`'s lower-level primitives, or a small custom C extension —
    a genuine gap named plainly, not glossed over as "just use
    `pgcrypto`."
  - **SQLite — an app-registered function, not really a "plugin."**
    `Microsoft.Data.Sqlite`'s `CreateFunction` already runs ordinary .NET
    code in the same process as the app tier — there is no real
    trust-boundary or memory-locality win over the default app-tier
    evaluator here, since it's the same process either way. Documented
    honestly as the weakest case of the three, not overclaimed as an
    equivalent "native plugin" the way SQL Server/PostgreSQL genuinely
    are.
  - **Real, stated operational cost, SQL Server and PostgreSQL
    specifically**: a native evaluator means the *database engine
    process itself* needs network access and credentials to the
    configured `IErasureKeyStore` backend (Vault/KMS) to decrypt —
    today, only the application tier needs that. This is a real
    expansion of the trust boundary, named explicitly as the price of
    the performance/memory win, not silently accepted.
- **Selection**: keyed DI per deployment, same registration model as
  every other seam (`ADR-059`) — a hosting team registers
  `AppTierEncryptedPredicateEvaluator` (default, ships in
  `EventStore.Abstractions`) or a future per-provider native
  implementation, never both active for the same query.

Consequences:
- `docs/extensibility-points.md` gains this row alongside
  `IErasureKeyStore`.
- No code ships this pass beyond the interface shape and the default
  app-tier implementation's design — `08-build-plan.md` gets a
  Not-started item for the default implementation (a small, real build,
  sequenced after `ADR-096`) and separate, later, explicitly-optional
  items for each native provider evaluator, not bundled into one.
- This is a build-plan sequencing choice, not an open design question —
  per `CLAUDE.md`'s own distinction between `TODO.md`/build-plan
  priority calls and `docs/10-open-questions.md`'s genuinely undecided
  forks, this ADR does not get a row in the latter.

**Implementation note, added 2026-08-27**: both native evaluators built
this session, `08-build-plan.md` item 56 — with genuinely different
confidence levels, stated plainly rather than glossed over.
- **SQL Server (`src/EventStore.SqlClr.SqlServer/`), built and verified.**
  A real technical constraint confirmed against Microsoft's own docs
  before writing any code: SQL Server's CLR host only ever loads .NET
  Framework assemblies, never .NET Core/.NET 5+ — this project targets
  `net48`, the one deliberate break from this solution's otherwise
  uniform `net10.0` targeting. `AesGcm` itself is .NET Standard 2.1+
  only and unavailable in classic .NET Framework — made usable here via
  [`Microsoft.Bcl.Cryptography`](https://www.nuget.org/packages/Microsoft.Bcl.Cryptography),
  a real first-party Microsoft package, verified this session to build
  and run correctly under `net48`. `EncryptedPredicateFunctions.
  DecryptAndCompareCore` is cross-verified against **real ciphertext
  produced by `EnvelopeAesGcm.Encrypt` under `net10.0`** (a golden fixture
  generated this session, not invented to match this file's own logic) —
  `tests/EventStore.SqlClr.SqlServer.Tests` passes against it, proving
  genuine cross-runtime interoperability, not mere self-consistency.
  Deployment script: `scripts/sql-clr/deploy-sql-server-encrypted-
  predicate-function.sql`, using `sys.sp_add_trusted_assembly` (the
  modern, SQL Server 2017 CU12+/2019+ mechanism for CLR strict security)
  rather than disabling that protection deployment-wide.
- **PostgreSQL (`scripts/sql-clr/deploy-postgres-encrypted-predicate-
  function.sql`), written but NOT verified.** Confirmed this session
  against PostgreSQL's own current docs: `pgcrypto`'s raw encrypt/decrypt
  functions support only CBC/ECB, no GCM/AEAD mode at all — genuine
  AES-256-GCM decrypt needs `plpython3u` (an untrusted procedural
  language) and Python's `cryptography` package, since hand-rolling
  AES-GCM's Galois-field multiplication in PL/pgSQL isn't practical.
  Unlike the SQL Server side, neither `plpython3u` nor the `cryptography`
  package exists in the standard Testcontainers `postgres` image this
  project's own integration tests already use — verifying this function
  end-to-end would mean building and maintaining a custom Postgres image,
  named here as real, separate, not-yet-done infrastructure work rather
  than silently assumed working by analogy to the verified SQL Server
  side.

  **Verified, `2026-09-04`.** A one-off custom image (`postgres:18`, the
  Debian-based tag — Alpine has no packaged `postgresql-plpython3-18` —
  plus `apt-get install postgresql-plpython3-18` and `pip install
  cryptography`) was built and run directly, the "real, separate
  infrastructure work" this note named. The deploy script, unmodified,
  cross-verified correctly against the exact same golden `EnvelopeAesGcm`
  ciphertext fixture the SQL Server side already uses (`tests/
  EventStore.SqlClr.SqlServer.Tests/EncryptedPredicateFunctionsTests.cs`)
  — all 8 assertions matched, including a wrong-key case returning
  `false` rather than raising. Full detail in `08-build-plan.md`, item
  56 (PostgreSQL half — see this ADR's own later additive note for why
  the item as a whole is not fully Done). The custom image itself was
  not kept as a permanent,
  CI-integrated fixture — folding real, ongoing `plpython3u` coverage
  into the Testcontainers-based suite remains real, separate work if
  ever wanted, not done by this verification pass.
- Both remain scoped to the `Local` `IErasureKeyStore`/
  `ISearchIndexKeyStore` backend only, per this ADR's own Decision — a
  Shared/`PerEntity` field backed by a real KMS/Vault cannot use either
  native evaluator without a different mechanism this ADR does not build.

**Additive note, 2026-09-04 — a third PostgreSQL implementation, real
performance numbers, and a real SQL Server deployment correction**
(direct request: "let's try these extensions out to see how they
perform"):

- **PostgreSQL**: added `scripts/sql-clr/postgres-c-extension/`, a
  genuinely native C/PGXS extension (OpenSSL EVP directly, no
  interpreter), verified against the same golden fixture. Real, measured
  `EXPLAIN ANALYZE` numbers (50,000 / 1,000,000 real `EnvelopeAesGcm`
  rows, one shared key): the C extension took ~33ms/~238ms, `plpython3u`
  took ~175ms/~3,133ms, and the app-tier default (fetch all candidates +
  decrypt in .NET) took ~82ms/~1,239ms. **`plpython3u` is slower than the
  app-tier default at both scales** — its own interpreter overhead
  outweighs the bandwidth it saves. The C extension is the clear winner
  and its advantage widens with scale (5.2×/13.2× at 1M rows), matching
  the theoretical case for in-database filtering. Recommendation: prefer
  the C extension over `plpython3u` going forward. Full numbers and
  reasoning in `08-build-plan.md`, item 56.
- **SQL Server — the "verified" claim above needed a real correction,
  which is now itself superseded: the real fix was built and verified
  the same session, direct request ("You need to build the sqlclr
  version with .net 4.8 with no net standard extensions").** The original
  claim rested on `tests/EventStore.SqlClr.SqlServer.Tests`, a plain
  net48 unit-test host process — never a live SQL Server CLR deployment.
  Actually attempting one (Docker, both default and Developer edition)
  found a genuine, structural platform blocker: `Microsoft.Bcl.
  Cryptography`'s own dependency chain fails SQL Server's CLR verifier
  under `SAFE` (confirmed across every available package version back to
  8.0.0, not a version-pinning fix — these packages use unsafe/
  unverifiable IL by design). Fixed by removing the package entirely:
  `src/EventStore.SqlClr.SqlServer/PureNet48AesGcm.cs` implements
  AES-256-GCM (NIST SP 800-38D) from scratch using only `System.
  Security.Cryptography.Aes`'s ECB single-block primitive — zero NuGet
  packages, zero .NET-Standard-only types.

  **A second, independent real deployment blocker was found and fixed
  the same pass**: with the polyfill chain gone, the assembly still threw
  a live `System.Security.HostProtectionException` — `Aes.Create()`
  returns a CAPI-backed `AesCryptoServiceProvider` under classic .NET
  Framework, carrying a `[HostProtection(Synchronization = true)]`
  attribute (it wraps a native OS crypto handle) that SQL Server's CLR
  host forbids even under `SAFE` — a different SQLCLR restriction class
  than the CLR-verifier failure the dependency removal fixed. Switched to
  `new AesManaged()` (fully managed, no native handle, no such
  restriction), and the real, live golden-fixture verification then
  passed completely — all 8 assertions correct against real
  `EnvelopeAesGcm`-produced ciphertext, on a real `mcr.microsoft.com/
  mssql/server:2022-latest` container, **default edition** (the earlier
  Linux/`UNSAFE` finding is now moot: `SAFE` alone is sufficient, nothing
  needs `UNSAFE` anymore). Both correctness bugs were verified the same
  way every cryptographic implementation in this design has been —
  successfully decrypting real, independently-produced ciphertext, not
  merely internal self-consistency; this has NOT had a dedicated
  cryptographic security review, the same genuine recommendation `ADR-097`
  already states for its own hand-built construction.

  Real, measured performance (same 50K/1M-row methodology as PostgreSQL
  above): SQLCLR took ~720ms/~780ms; the app-tier default took ~102ms/
  ~1,640ms. **A genuine crossover, not predicted going in**: SQLCLR is
  slower than app-tier at 50,000 rows (the same surprising pattern
  `plpython3u` showed on PostgreSQL — real per-call SQLCLR host overhead),
  but its own elapsed time barely grows from 50K to 1M rows while
  app-tier's scales with volume, because SQL Server's query engine
  parallelizes the larger scan across cores (`CPU time` ~22s vs.
  `elapsed time` ~780ms at 1M rows makes this directly visible). Full
  numbers and reasoning in `08-build-plan.md`, item 56.

**Final additive note, 2026-09-04 — a batch table-valued function
variant, the final verdict, and the spike-folder move** (direct
request: "what about a version that would use a sqlclr table function or
stored procedure so they are processed in blocks? ive done vector
processing in the database and has better performance", followed by
"I dont think the performance of this feature is worth the effort... I
would like this branch left behind as a POC"):

- **Batch table-valued function, built and measured for real.** A
  `[SqlFunction(FillRowMethodName = ..., TableDefinition = ...,
  DataAccess = DataAccessKind.Read)]` TVF
  (`DecryptAndCompareBatchBench`) querying its own candidate rows via
  `"context connection=true"` and decrypting+comparing all of them in one
  managed loop, avoiding the scalar function's per-row SQLOS↔CLR crossing
  entirely. Confirmed first, via research rather than assumption, that
  SQL Server cannot accept a table-valued parameter as CLR routine input
  — the batch function reads its own candidate set instead of receiving
  one. Real, measured numbers: 50,000 rows — batch ~418ms vs. scalar
  ~720ms (batching genuinely wins, confirming the user's own prior
  experience with in-database vector processing); 1,000,000 rows — batch
  ~9,300ms vs. scalar ~780ms (batching loses badly). Root cause,
  confirmed against Microsoft's own docs before writing anything up: SQL
  Server forces CLR table-valued functions that declare real data access
  to run **serially** — the scalar function's crossover win at 1M rows
  (above) came entirely from the query engine parallelizing the plan
  across cores, an option the batch TVF's own architecture forecloses.
  Batching helps at equal parallelism; it does not help once it trades
  away parallelism to get there. Full numbers and reasoning in
  `08-build-plan.md`, item 56.
- **Final verdict: not adopted.** No implementation tried — `plpython3u`,
  the SQL Server scalar function, the SQL Server batch TVF — beat the
  already-built, already-simple app-tier default consistently across
  scale; the PostgreSQL C extension is a clear, consistent win but was
  never wired into a real `IEncryptedPredicateEvaluator`, and adopting a
  C extension carries its own ongoing toolchain cost this investigation
  didn't weigh against "the app-tier default already works." Direct
  request: treat this branch as a proof-of-concept, not headed for
  adoption or merge. A possible future direction, named honestly rather
  than pursued: `unsafe` pointer arithmetic in `PureNet48AesGcm`'s hot
  `GHASH` loop could plausibly close some of the SQLCLR gap, but would
  very likely need `PERMISSION_SET = UNSAFE` to load at all, reopening
  the SQL Server Linux/Testcontainers deployability question
  `PureNet48AesGcm.cs` was built specifically to close — not attempted.
- **All native-evaluator code moved to
  `spikes/in-database-native-predicate-evaluators/`** (direct request:
  "actaully put these implmetnation in the spkie folder"), out of
  `src/`/`tests/`/`scripts/sql-clr/` and out of `EventStore.slnx` —
  matching this repo's existing `spikes/` convention (real, working,
  independently-buildable code, not wired into the main solution). The
  paths cited earlier in this ADR (`src/EventStore.SqlClr.SqlServer/`,
  `tests/EventStore.SqlClr.SqlServer.Tests`, `scripts/sql-clr/deploy-*.
  sql`) are historical — as of this move they resolve under
  `spikes/in-database-native-predicate-evaluators/SqlServerSqlClrSpike/`,
  `.../SqlServerSqlClrSpike.Tests/`, and `.../deploy-*.sql` respectively;
  see that folder's own `README.md` for the current at-a-glance summary
  of every implementation and its real, measured result. The
  already-adopted app-tier default (`AppTierEncryptedPredicateEvaluator`)
  is unaffected and remains in `src/EventStore.Erasure/` — this move and
  verdict only concern the native alternatives this ADR itself scoped as
  optional.
