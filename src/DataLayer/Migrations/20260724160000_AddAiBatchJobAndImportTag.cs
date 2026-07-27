using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fistix.TaskManager.DataLayer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EfContext))]
    [Migration("20260724160000_AddAiBatchJobAndImportTag")]
    public partial class AddAiBatchJobAndImportTag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportTag",
                table: "TodoTask",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiBatchJob",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepsCsv = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurrentStep = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TodoExternalIdsJson = table.Column<string>(type: "text", nullable: false),
                    Cursor = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    DelayMsBetweenItems = table.Column<int>(type: "integer", nullable: false),
                    OnlyMissing = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    ImportTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastTodoExternalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PausedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiBatchJob", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TodoTask_CreatedByUserId_ImportTag",
                table: "TodoTask",
                columns: new[] { "CreatedByUserId", "ImportTag" });

            migrationBuilder.CreateIndex(
                name: "IX_AiBatchJob_CreatedByUserId",
                table: "AiBatchJob",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiBatchJob_ExternalId",
                table: "AiBatchJob",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiBatchJob_Status",
                table: "AiBatchJob",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiBatchJob");

            migrationBuilder.DropIndex(
                name: "IX_TodoTask_CreatedByUserId_ImportTag",
                table: "TodoTask");

            migrationBuilder.DropColumn(
                name: "ImportTag",
                table: "TodoTask");
        }
    }
}
