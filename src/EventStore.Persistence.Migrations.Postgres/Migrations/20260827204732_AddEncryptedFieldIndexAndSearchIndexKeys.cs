using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedFieldIndexAndSearchIndexKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IndexKind",
                table: "FilterableFields",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SearchableConfig",
                table: "FilterableFields",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EncryptedFieldIndexEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppId = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    EventTypeName = table.Column<string>(type: "text", nullable: false),
                    FieldJsonPath = table.Column<string>(type: "text", nullable: false),
                    IndexKind = table.Column<int>(type: "integer", nullable: false),
                    Granularity = table.Column<string>(type: "text", nullable: true),
                    Token = table.Column<string>(type: "text", nullable: false),
                    StoredEventSequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncryptedFieldIndexEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalSearchIndexKeyMaterials",
                columns: table => new
                {
                    KeyReference = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalSearchIndexKeyMaterials", x => x.KeyReference);
                });

            migrationBuilder.CreateTable(
                name: "SearchIndexKeys",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "text", nullable: false),
                    EventTypeName = table.Column<string>(type: "text", nullable: false),
                    FieldJsonPath = table.Column<string>(type: "text", nullable: false),
                    KeyReference = table.Column<string>(type: "text", nullable: false),
                    BackendName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexKeys", x => new { x.AppId, x.EventTypeName, x.FieldJsonPath });
                });

            migrationBuilder.CreateIndex(
                name: "IX_EncryptedFieldIndexEntries_AppId_EventTypeName_FieldJsonPat~",
                table: "EncryptedFieldIndexEntries",
                columns: new[] { "AppId", "EventTypeName", "FieldJsonPath", "Token" });

            migrationBuilder.CreateIndex(
                name: "IX_EncryptedFieldIndexEntries_EntityId",
                table: "EncryptedFieldIndexEntries",
                column: "EntityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EncryptedFieldIndexEntries");

            migrationBuilder.DropTable(
                name: "LocalSearchIndexKeyMaterials");

            migrationBuilder.DropTable(
                name: "SearchIndexKeys");

            migrationBuilder.DropColumn(
                name: "IndexKind",
                table: "FilterableFields");

            migrationBuilder.DropColumn(
                name: "SearchableConfig",
                table: "FilterableFields");
        }
    }
}
