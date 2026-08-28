using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Fistix.TaskManager.DataLayer.Migrations
{
    [DbContext(typeof(EfContext))]
    [Migration("20260816120000_AddKnowledgeLab")]
    public partial class AddKnowledgeLab : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeDocument",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExtractedText = table.Column<string>(type: "text", nullable: true),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExternalId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeDocument", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeChunk",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Heading = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExternalId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeChunk", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeChunk_KnowledgeDocument_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "KnowledgeDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeIngestJob",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStep = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ChunksEmbedded = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeIngestJob", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeIngestJob_KnowledgeDocument_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "KnowledgeDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeChunkEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChunkId = table.Column<int>(type: "integer", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(384)", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeChunkEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeChunkEmbeddings_KnowledgeChunk_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "KnowledgeChunk",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocument_CreatedByUserId",
                table: "KnowledgeDocument",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocument_ExternalId",
                table: "KnowledgeDocument",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocument_Status",
                table: "KnowledgeDocument",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunk_DocumentId_Ordinal",
                table: "KnowledgeChunk",
                columns: new[] { "DocumentId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunk_ExternalId",
                table: "KnowledgeChunk",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunkEmbeddings_ChunkId_EmbeddingModel",
                table: "KnowledgeChunkEmbeddings",
                columns: new[] { "ChunkId", "EmbeddingModel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeIngestJob_CreatedByUserId",
                table: "KnowledgeIngestJob",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeIngestJob_DocumentId",
                table: "KnowledgeIngestJob",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeIngestJob_ExternalId",
                table: "KnowledgeIngestJob",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeIngestJob_Status",
                table: "KnowledgeIngestJob",
                column: "Status");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "KnowledgeChunkEmbeddings");
            migrationBuilder.DropTable(name: "KnowledgeIngestJob");
            migrationBuilder.DropTable(name: "KnowledgeChunk");
            migrationBuilder.DropTable(name: "KnowledgeDocument");
        }
    }
}
