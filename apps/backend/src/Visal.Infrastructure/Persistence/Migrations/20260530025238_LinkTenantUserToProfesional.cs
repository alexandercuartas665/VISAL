using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkTenantUserToProfesional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "profesional_id",
                table: "tenant_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_profesional_id",
                table: "tenant_users",
                column: "profesional_id",
                unique: true,
                filter: "profesional_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_users_profesionales_profesional_id",
                table: "tenant_users",
                column: "profesional_id",
                principalTable: "profesionales",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenant_users_profesionales_profesional_id",
                table: "tenant_users");

            migrationBuilder.DropIndex(
                name: "ix_tenant_users_profesional_id",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "profesional_id",
                table: "tenant_users");
        }
    }
}
