using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicamentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medicamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    producto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    titular = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    registro_sanitario = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    fecha_expedicion = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: true),
                    estado_registro = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    expediente_cum = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    consecutivo_cum = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cantidad_cum = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    descripcion_comercial = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    estado_cum = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    fecha_activo = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_inactivo = table.Column<DateOnly>(type: "date", nullable: true),
                    muestra_medica = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    unidad = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    atc = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    descripcion_atc = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    via_administracion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    concentracion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    principio_activo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    unidad_medida = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cantidad = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    unidad_referencia = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    forma_farmaceutica = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    nombre_rol = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    tipo_rol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    modalidad = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ium = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medicamentos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_medicamentos_tenant_id_atc",
                table: "medicamentos",
                columns: new[] { "tenant_id", "atc" });

            migrationBuilder.CreateIndex(
                name: "ix_medicamentos_tenant_id_ium",
                table: "medicamentos",
                columns: new[] { "tenant_id", "ium" });

            migrationBuilder.CreateIndex(
                name: "ix_medicamentos_tenant_id_producto",
                table: "medicamentos",
                columns: new[] { "tenant_id", "producto" });

            migrationBuilder.CreateIndex(
                name: "ix_medicamentos_tenant_id_registro_sanitario",
                table: "medicamentos",
                columns: new[] { "tenant_id", "registro_sanitario" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medicamentos");
        }
    }
}
