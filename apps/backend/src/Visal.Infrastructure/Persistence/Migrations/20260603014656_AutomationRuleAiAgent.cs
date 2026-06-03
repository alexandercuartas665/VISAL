using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationRuleAiAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ai_agent_id",
                table: "automation_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_automation_rules_ai_agent_id",
                table: "automation_rules",
                column: "ai_agent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_automation_rules_ai_agents_ai_agent_id",
                table: "automation_rules",
                column: "ai_agent_id",
                principalTable: "ai_agents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_automation_rules_ai_agents_ai_agent_id",
                table: "automation_rules");

            migrationBuilder.DropIndex(
                name: "ix_automation_rules_ai_agent_id",
                table: "automation_rules");

            migrationBuilder.DropColumn(
                name: "ai_agent_id",
                table: "automation_rules");
        }
    }
}
