-- ADR-098 -- deploys the SQL Server half of the in-database native
-- predicate evaluator seam (src/EventStore.SqlClr.SqlServer). Uses
-- sys.sp_add_trusted_assembly (SQL Server 2017 CU12+/2019+), the modern,
-- certificate-free mechanism CLR strict security's own default posture
-- expects -- deliberately NOT `sp_configure 'clr strict security', 0`,
-- which would blanket-disable that protection for every assembly, not
-- just this one.
--
-- Corrected, 2026-09-04, direct request ("build the sqlclr version with
-- .net 4.8 with no net standard extensions") -- this used to also
-- reference Microsoft.Bcl.Cryptography.dll/System.Formats.Asn1.dll as
-- real, required dependencies. Both are REMOVED: their own transitive
-- dependency chain (System.Buffers/System.Memory/System.Numerics.
-- Vectors/System.Runtime.CompilerServices.Unsafe) was found, by actually
-- attempting a live deployment, to fail SQL Server's CLR verifier under
-- PERMISSION_SET = SAFE in every available version (docs/adrs/adr-098-
-- *.md's own 2026-09-04 additive note has the full investigation).
-- PureNet48AesGcm.cs now implements AES-256-GCM from scratch using only
-- System.Security.Cryptography.Aes's ECB single-block primitive -- this
-- deploys ONE assembly, with ZERO dependencies to trust or install
-- alongside it.
--
-- Live-verified for real this same pass (Docker, mcr.microsoft.com/
-- mssql/server:2022-latest, default edition -- Developer/Enterprise is
-- NOT required, PERMISSION_SET = SAFE alone is genuinely sufficient now)
-- against the exact golden EnvelopeAesGcm ciphertext fixture tests/
-- EventStore.SqlClr.SqlServer.Tests/EncryptedPredicateFunctionsTests.cs
-- already uses -- all 8 assertions matched.
--
-- Prerequisite: build EventStore.SqlClr.SqlServer.csproj (net48; the
-- output is exactly one DLL, no others to copy) and copy EventStore.
-- SqlClr.SqlServer.dll to a path this SQL Server instance can read.
-- The path below uses a Linux-container-style path (this project's own
-- real Testcontainers/Docker deployment target, e.g.
-- /var/opt/mssql/EventStoreSqlClr/EventStore.SqlClr.SqlServer.dll) --
-- substitute a Windows path (e.g. C:\EventStoreSqlClr\...) if deploying
-- to a real Windows-hosted instance instead.
--
-- Scope, stated plainly (ADR-098): this function only ever decrypts
-- ciphertext produced by the "Local" IErasureKeyStore/ISearchIndexKeyStore
-- backend -- the calling query supplies the raw key bytes itself (read
-- from LocalErasureKeyMaterials/LocalSearchIndexKeyMaterials, ordinary
-- tables in the SAME database), so this function never needs network
-- access to a real KMS/Vault. A Shared/PerEntity-scope field backed by a
-- cloud KMS or HashiCorp Vault cannot use this native evaluator without a
-- different mechanism this ADR does not build.

EXEC sp_configure 'clr enabled', 1;
RECONFIGURE;
GO

DECLARE @AssemblyPath NVARCHAR(4000) = N'/var/opt/mssql/EventStoreSqlClr/EventStore.SqlClr.SqlServer.dll';
DECLARE @AssemblyHash VARBINARY(64) = HASHBYTES('SHA2_512', (
    SELECT BulkColumn FROM OPENROWSET(BULK '/var/opt/mssql/EventStoreSqlClr/EventStore.SqlClr.SqlServer.dll', SINGLE_BLOB) AS x
));

-- Trust this exact assembly by its own hash -- narrower and more
-- auditable than disabling CLR strict security deployment-wide. No
-- other assembly needs trusting: zero dependencies, by design.
EXEC sys.sp_add_trusted_assembly @hash = @AssemblyHash, @description = N'EventStore.SqlClr.SqlServer (ADR-098, pure net48)';
GO

CREATE ASSEMBLY [EventStore.SqlClr.SqlServer] FROM '/var/opt/mssql/EventStoreSqlClr/EventStore.SqlClr.SqlServer.dll' WITH PERMISSION_SET = SAFE;
GO

-- Pure computation, no I/O -- PERMISSION_SET = SAFE is genuinely enough;
-- no EXTERNAL_ACCESS/UNSAFE escalation needed, unlike a hypothetical
-- evaluator that reached out to a real KMS/Vault itself.
CREATE FUNCTION dbo.fn_DecryptAndCompare
(
    @ciphertextBase64 NVARCHAR(MAX),
    @key VARBINARY(MAX),
    @dataType NVARCHAR(20),      -- 'Number' | 'DateTimeOffset' | 'String'
    @comparisonOperator NVARCHAR(5), -- 'gt' | 'gte' | 'lt' | 'lte'
    @comparisonValue NVARCHAR(MAX)
)
RETURNS BIT
AS EXTERNAL NAME [EventStore.SqlClr.SqlServer].[EventStore.SqlClr.SqlServer.EncryptedPredicateFunctions].[DecryptAndCompare];
GO

-- Example query shape (ADR-096's own bucket-narrowing already ran; this
-- is the exact-match step ADR-098 exists for, over an already-small
-- candidate set -- never a full-table scan):
--
-- SELECT e.SequenceNumber
-- FROM Events e
-- JOIN EncryptedFieldIndexEntries idx ON idx.EntityId = e.EntityId AND idx.FieldJsonPath = @fieldJsonPath
-- JOIN EntityErasureKeys k ON k.EntityId = e.EntityId
-- JOIN LocalErasureKeyMaterials m ON m.KeyReference = k.KeyReference
-- WHERE e.SequenceNumber IN (@candidateSequenceNumbers)
--   AND dbo.fn_DecryptAndCompare(JSON_VALUE(e.Payload, @fieldJsonPath), m.WrappedKey, @dataType, @comparisonOperator, @comparisonValue) = 1;
