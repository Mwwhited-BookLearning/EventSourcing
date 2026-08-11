using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
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
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    KeyReference = table.Column<string>(type: "text", nullable: false),
                    BackendName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ErasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityErasureKeys", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "LocalErasureKeyMaterials",
                columns: table => new
                {
                    KeyReference = table.Column<string>(type: "text", nullable: false),
                    WrappedKey = table.Column<byte[]>(type: "bytea", nullable: true),
                    Destroyed = table.Column<bool>(type: "boolean", nullable: false)
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
