using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAseguradoras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aseguradoras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    codigo_movilidad = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    nit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    regimen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cod_int = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aseguradoras", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contratos_aseguradora",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aseguradora_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_contrato = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    fecha_inicial = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_final = table.Column<DateOnly>(type: "date", nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prorroga = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_aseguradora", x => x.id);
                    table.ForeignKey(
                        name: "fk_contratos_aseguradora_aseguradoras_aseguradora_id",
                        column: x => x.aseguradora_id,
                        principalTable: "aseguradoras",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "servicios_contrato",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sede = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    historia = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    codigo_servicio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    codigo_interno = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tarifa = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    modulo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    especialidad = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    modalidad = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    clasificacion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    observaciones = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_servicios_contrato", x => x.id);
                    table.ForeignKey(
                        name: "fk_servicios_contrato_contratos_aseguradora_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contratos_aseguradora",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aseguradoras_tenant_id_codigo",
                table: "aseguradoras",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_aseguradora_aseguradora_id",
                table: "contratos_aseguradora",
                column: "aseguradora_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_aseguradora_tenant_id_aseguradora_id",
                table: "contratos_aseguradora",
                columns: new[] { "tenant_id", "aseguradora_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contratos_aseguradora_tenant_id_codigo_contrato",
                table: "contratos_aseguradora",
                columns: new[] { "tenant_id", "codigo_contrato" });

            migrationBuilder.CreateIndex(
                name: "ix_servicios_contrato_contrato_id",
                table: "servicios_contrato",
                column: "contrato_id");

            migrationBuilder.CreateIndex(
                name: "ix_servicios_contrato_tenant_id_contrato_id",
                table: "servicios_contrato",
                columns: new[] { "tenant_id", "contrato_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "servicios_contrato");

            migrationBuilder.DropTable(
                name: "contratos_aseguradora");

            migrationBuilder.DropTable(
                name: "aseguradoras");
        }
    }
}
