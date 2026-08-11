using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddChainCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessLogChainCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SequenceNumberRangeStart = table.Column<long>(type: "INTEGER", nullable: false),
                    SequenceNumberRangeEnd = table.Column<long>(type: "INTEGER", nullable: false),
                    ChainHashAtRangeEnd = table.Column<string>(type: "TEXT", nullable: false),
                    ContentProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ContentProviderRef = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogChainCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventLogChainCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SequenceNumberRangeStart = table.Column<long>(type: "INTEGER", nullable: false),
                    SequenceNumberRangeEnd = table.Column<long>(type: "INTEGER", nullable: false),
                    ChainHashAtRangeEnd = table.Column<string>(type: "TEXT", nullable: false),
                    ContentProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ContentProviderRef = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventLogChainCheckpoints", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLogChainCheckpoints");

            migrationBuilder.DropTable(
                name: "EventLogChainCheckpoints");
        }
    }
}
