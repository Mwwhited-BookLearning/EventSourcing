using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Sqlite.Migrations
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: true),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentRefs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    Bytes = table.Column<byte[]>(type: "BLOB", nullable: true),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ContentProviderKey = table.Column<string>(type: "TEXT", nullable: true),
                    ContentProviderRef = table.Column<string>(type: "TEXT", nullable: true),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ChunkIndex = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredReadClaim = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredPublishClaim = table.Column<string>(type: "TEXT", nullable: true)
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
