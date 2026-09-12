using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteEsPrueba : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_prueba",
                table: "pacientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_pacientes_tenant_id_es_prueba",
                table: "pacientes",
                columns: new[] { "tenant_id", "es_prueba" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pacientes_tenant_id_es_prueba",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "es_prueba",
                table: "pacientes");
        }
    }
}
