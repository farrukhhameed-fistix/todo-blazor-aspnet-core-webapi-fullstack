using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fistix.TaskManager.DataLayer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EfContext))]
    [Migration("20260904120000_AddKnowledgeChunkSearchVector")]
    public partial class AddKnowledgeChunkSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "KnowledgeChunk"
                ADD COLUMN IF NOT EXISTS "SearchVector" tsvector
                GENERATED ALWAYS AS (
                  to_tsvector('english', coalesce("Heading", '') || ' ' || coalesce("Content", ''))
                ) STORED;

                CREATE INDEX IF NOT EXISTS "IX_KnowledgeChunk_SearchVector"
                ON "KnowledgeChunk" USING GIN ("SearchVector");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_KnowledgeChunk_SearchVector";
                ALTER TABLE "KnowledgeChunk" DROP COLUMN IF EXISTS "SearchVector";
                """);
        }
    }
}
