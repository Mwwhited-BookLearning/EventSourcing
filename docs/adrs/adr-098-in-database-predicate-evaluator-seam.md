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
  56 (now `Done`). The custom image itself was not kept as a permanent,
  CI-integrated fixture — folding real, ongoing `plpython3u` coverage
  into the Testcontainers-based suite remains real, separate work if
  ever wanted, not done by this verification pass.
- Both remain scoped to the `Local` `IErasureKeyStore`/
  `ISearchIndexKeyStore` backend only, per this ADR's own Decision — a
  Shared/`PerEntity` field backed by a real KMS/Vault cannot use either
  native evaluator without a different mechanism this ADR does not build.
