using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamingChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RedactedRanges",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "text", nullable: false),
                    FromTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ToTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequiredClaim = table.Column<string>(type: "text", nullable: false),
                    Strategy = table.Column<string>(type: "text", nullable: false),
                    ShowFirst = table.Column<int>(type: "integer", nullable: true),
                    ShowLast = table.Column<int>(type: "integer", nullable: true),
                    MaskChar = table.Column<char>(type: "character(1)", nullable: true),
                    PreserveSeparators = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedactedRanges", x => new { x.ChannelId, x.FromTimestamp });
                });

            migrationBuilder.CreateTable(
                name: "TelemetryChannels",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "text", nullable: false),
                    AppId = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    ContentKind = table.Column<int>(type: "integer", nullable: false),
                    SampleType = table.Column<int>(type: "integer", nullable: true),
                    MimeType = table.Column<string>(type: "text", nullable: true),
                    SampleIntervalMicros = table.Column<long>(type: "bigint", nullable: true),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    ThreadId = table.Column<string>(type: "text", nullable: true),
                    SourceChannelIds = table.Column<string>(type: "text", nullable: true),
                    TransformKind = table.Column<string>(type: "text", nullable: true),
                    RequiredReadClaim = table.Column<string>(type: "text", nullable: true),
                    LastAppliedLogicalTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastBatchReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSampleTimestampReceived = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryChannels", x => x.ChannelId);
                });

            migrationBuilder.CreateTable(
                name: "TelemetrySamples",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MonotonicElapsedMicros = table.Column<long>(type: "bigint", nullable: true),
                    Value = table.Column<byte[]>(type: "bytea", nullable: false),
                    LateArrivalFlag = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetrySamples", x => new { x.ChannelId, x.Timestamp });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryChannels_ThreadId",
                table: "TelemetryChannels",
                column: "ThreadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RedactedRanges");

            migrationBuilder.DropTable(
                name: "TelemetryChannels");

            migrationBuilder.DropTable(
                name: "TelemetrySamples");
        }
    }
}
