using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddBinaryAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttachmentRefs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentRefs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    ContentHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ContentProviderKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentProviderRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChunkIndex = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequiredReadClaim = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequiredPublishClaim = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.ContentHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentRefs_ContentHash",
                table: "AttachmentRefs",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentRefs_EntityId",
                table: "AttachmentRefs",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentRefs_EventId",
                table: "AttachmentRefs",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttachmentRefs");

            migrationBuilder.DropTable(
                name: "Attachments");
        }
    }
}
