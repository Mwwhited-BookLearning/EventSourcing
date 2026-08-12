using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
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
                type: "nvarchar(max)",
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
