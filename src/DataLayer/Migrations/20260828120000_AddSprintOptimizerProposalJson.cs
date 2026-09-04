using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fistix.TaskManager.DataLayer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EfContext))]
    [Migration("20260828120000_AddSprintOptimizerProposalJson")]
    public partial class AddSprintOptimizerProposalJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProposalJson",
                table: "SprintOptimizerJob",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposalJson",
                table: "SprintOptimizerJob");
        }
    }
}
