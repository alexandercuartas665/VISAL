using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCuotaCopagoYPagoAsignacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "categoria_copago",
                table: "asignaciones",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo_pago",
                table: "asignaciones",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "valor_pago_real",
                table: "asignaciones",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "valor_pago_sugerido",
                table: "asignaciones",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cuotas_copagos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    categoria = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    valor_sugerido = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuotas_copagos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuotas_copagos_tenant_id_tipo_categoria",
                table: "cuotas_copagos",
                columns: new[] { "tenant_id", "tipo", "categoria" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cuotas_copagos");

            migrationBuilder.DropColumn(
                name: "categoria_copago",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "tipo_pago",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "valor_pago_real",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "valor_pago_sugerido",
                table: "asignaciones");
        }
    }
}
