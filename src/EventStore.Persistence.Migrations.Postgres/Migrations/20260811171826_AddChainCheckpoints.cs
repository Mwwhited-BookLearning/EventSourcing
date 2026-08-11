using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SequenceNumberRangeStart = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNumberRangeEnd = table.Column<long>(type: "bigint", nullable: false),
                    ChainHashAtRangeEnd = table.Column<string>(type: "text", nullable: false),
                    ContentProviderKey = table.Column<string>(type: "text", nullable: false),
                    ContentProviderRef = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogChainCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventLogChainCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SequenceNumberRangeStart = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNumberRangeEnd = table.Column<long>(type: "bigint", nullable: false),
                    ChainHashAtRangeEnd = table.Column<string>(type: "text", nullable: false),
                    ContentProviderKey = table.Column<string>(type: "text", nullable: false),
                    ContentProviderRef = table.Column<string>(type: "text", nullable: false)
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
