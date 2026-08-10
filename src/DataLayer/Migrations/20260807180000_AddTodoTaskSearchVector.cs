using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fistix.TaskManager.DataLayer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EfContext))]
    [Migration("20260807180000_AddTodoTaskSearchVector")]
    public partial class AddTodoTaskSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "TodoTask"
                ADD COLUMN IF NOT EXISTS "SearchVector" tsvector
                GENERATED ALWAYS AS (
                  to_tsvector('english', coalesce("Title", '') || ' ' || coalesce("Description", ''))
                ) STORED;

                CREATE INDEX IF NOT EXISTS "IX_TodoTask_SearchVector"
                ON "TodoTask" USING GIN ("SearchVector");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_TodoTask_SearchVector";
                ALTER TABLE "TodoTask" DROP COLUMN IF EXISTS "SearchVector";
                """);
        }
    }
}
