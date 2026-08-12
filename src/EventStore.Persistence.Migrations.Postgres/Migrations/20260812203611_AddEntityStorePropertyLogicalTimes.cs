using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityStorePropertyLogicalTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PropertyLogicalTimes",
                table: "EntityStore",
                type: "text",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PropertyLogicalTimes",
                table: "EntityStore");
        }
    }
}
