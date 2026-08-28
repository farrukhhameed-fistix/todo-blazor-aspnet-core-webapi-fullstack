using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fistix.TaskManager.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintOptimizerCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckpointJson",
                table: "SprintOptimizerJob",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingRequestId",
                table: "SprintOptimizerJob",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckpointJson",
                table: "SprintOptimizerJob");

            migrationBuilder.DropColumn(
                name: "PendingRequestId",
                table: "SprintOptimizerJob");
        }
    }
}
