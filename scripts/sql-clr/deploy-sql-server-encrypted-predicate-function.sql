-- ADR-098 -- deploys the SQL Server half of the in-database native
-- predicate evaluator seam (src/EventStore.SqlClr.SqlServer). Uses
-- sys.sp_add_trusted_assembly (SQL Server 2017 CU12+/2019+), the modern,
-- certificate-free mechanism CLR strict security's own default posture
-- expects -- deliberately NOT `sp_configure 'clr strict security', 0`,
-- which would blanket-disable that protection for every assembly, not
-- just this one.
--
-- Prerequisite: build EventStore.SqlClr.SqlServer.csproj (net48) and copy
-- EventStore.SqlClr.SqlServer.dll, plus its Microsoft.Bcl.Cryptography.dll
-- and System.Formats.Asn1.dll dependencies (see the project's own build
-- output), to a path this SQL Server instance can read.
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

DECLARE @AssemblyPath NVARCHAR(4000) = N'C:\EventStoreSqlClr\EventStore.SqlClr.SqlServer.dll';
DECLARE @AssemblyHash VARBINARY(64) = HASHBYTES('SHA2_512', (
    SELECT BulkColumn FROM OPENROWSET(BULK 'C:\EventStoreSqlClr\EventStore.SqlClr.SqlServer.dll', SINGLE_BLOB) AS x
));

-- Trust this exact assembly by its own hash -- narrower and more
-- auditable than disabling CLR strict security deployment-wide.
EXEC sys.sp_add_trusted_assembly @hash = @AssemblyHash, @description = N'EventStore.SqlClr.SqlServer (ADR-098)';

-- Microsoft.Bcl.Cryptography (and its own transitive System.Formats.Asn1)
-- must be trusted the same way -- CREATE ASSEMBLY resolves dependencies
-- from the same directory as the main assembly by default; each still
-- needs its own trusted-assembly entry under CLR strict security.
DECLARE @BclCryptoHash VARBINARY(64) = HASHBYTES('SHA2_512', (
    SELECT BulkColumn FROM OPENROWSET(BULK 'C:\EventStoreSqlClr\Microsoft.Bcl.Cryptography.dll', SINGLE_BLOB) AS x
));
EXEC sys.sp_add_trusted_assembly @hash = @BclCryptoHash, @description = N'Microsoft.Bcl.Cryptography (ADR-098 dependency)';

DECLARE @Asn1Hash VARBINARY(64) = HASHBYTES('SHA2_512', (
    SELECT BulkColumn FROM OPENROWSET(BULK 'C:\EventStoreSqlClr\System.Formats.Asn1.dll', SINGLE_BLOB) AS x
));
EXEC sys.sp_add_trusted_assembly @hash = @Asn1Hash, @description = N'System.Formats.Asn1 (ADR-098 transitive dependency)';

CREATE ASSEMBLY [Microsoft.Bcl.Cryptography] FROM 'C:\EventStoreSqlClr\Microsoft.Bcl.Cryptography.dll' WITH PERMISSION_SET = SAFE;
CREATE ASSEMBLY [System.Formats.Asn1] FROM 'C:\EventStoreSqlClr\System.Formats.Asn1.dll' WITH PERMISSION_SET = SAFE;
CREATE ASSEMBLY [EventStore.SqlClr.SqlServer] FROM 'C:\EventStoreSqlClr\EventStore.SqlClr.SqlServer.dll' WITH PERMISSION_SET = SAFE;
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
