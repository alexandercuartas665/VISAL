using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodigoSecundarioToFormDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo_secundario",
                table: "form_definitions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_definitions_tenant_id_codigo_secundario",
                table: "form_definitions",
                columns: new[] { "tenant_id", "codigo_secundario" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_form_definitions_tenant_id_codigo_secundario",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "codigo_secundario",
                table: "form_definitions");
        }
    }
}
