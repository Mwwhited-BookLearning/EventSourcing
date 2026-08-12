using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityStorePropertyVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PropertyVersions",
                table: "EntityStore",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PropertyVersions",
                table: "EntityStore");
        }
    }
}
