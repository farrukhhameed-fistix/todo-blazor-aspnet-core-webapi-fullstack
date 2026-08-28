using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fistix.TaskManager.DataLayer.Migrations
{
    /// <inheritdoc />
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
