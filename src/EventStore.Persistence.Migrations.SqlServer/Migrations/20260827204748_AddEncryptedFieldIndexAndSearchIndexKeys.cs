using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
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
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SearchableConfig",
                table: "FilterableFields",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EncryptedFieldIndexEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventTypeName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FieldJsonPath = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IndexKind = table.Column<int>(type: "int", nullable: false),
                    Granularity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Token = table.Column<string>(type: "nvarchar(450)", nullable: false),
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
                    KeyReference = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Key = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalSearchIndexKeyMaterials", x => x.KeyReference);
                });

            migrationBuilder.CreateTable(
                name: "SearchIndexKeys",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventTypeName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FieldJsonPath = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    KeyReference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BackendName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexKeys", x => new { x.AppId, x.EventTypeName, x.FieldJsonPath });
                });

            migrationBuilder.CreateIndex(
                name: "IX_EncryptedFieldIndexEntries_AppId_EventTypeName_FieldJsonPath_Token",
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
