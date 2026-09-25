using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RdaEnvioActivoPorSede : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: true — las sedes con credencial que HOY ya envian
            // automaticamente deben seguir enviando tras el deploy (no apagar nada
            // en silencio). El operador desactiva manualmente las que no quiera.
            migrationBuilder.AddColumn<bool>(
                name: "envio_activo",
                table: "interoperabilidad_credenciales_sede",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "envio_activo",
                table: "interoperabilidad_credenciales_sede");
        }
    }
}
