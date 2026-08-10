using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddErasureKeyEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntityErasureKeys",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    KeyReference = table.Column<string>(type: "TEXT", nullable: false),
                    BackendName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ErasedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityErasureKeys", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "LocalErasureKeyMaterials",
                columns: table => new
                {
                    KeyReference = table.Column<string>(type: "TEXT", nullable: false),
                    WrappedKey = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Destroyed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalErasureKeyMaterials", x => x.KeyReference);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntityErasureKeys");

            migrationBuilder.DropTable(
                name: "LocalErasureKeyMaterials");
        }
    }
}
