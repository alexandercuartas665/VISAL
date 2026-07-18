using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacturacionCamposF4a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo_habilitacion",
                table: "sucursales",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "causa_externa",
                table: "servicios_contrato",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "finalidad",
                table: "servicios_contrato",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "grupo_servicios",
                table: "servicios_contrato",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "modalidad_atencion",
                table: "servicios_contrato",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "servicios",
                table: "servicios_contrato",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "valor_total",
                table: "servicios_contrato",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "via_ingreso",
                table: "servicios_contrato",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cups_representativo_servicio_id",
                table: "paquetes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correo_facturacion",
                table: "aseguradoras",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_paquetes_cups_representativo_servicio_id",
                table: "paquetes",
                column: "cups_representativo_servicio_id");

            migrationBuilder.AddForeignKey(
                name: "fk_paquetes_paquete_servicios_cups_representativo_servicio_id",
                table: "paquetes",
                column: "cups_representativo_servicio_id",
                principalTable: "paquete_servicios",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_paquetes_paquete_servicios_cups_representativo_servicio_id",
                table: "paquetes");

            migrationBuilder.DropIndex(
                name: "ix_paquetes_cups_representativo_servicio_id",
                table: "paquetes");

            migrationBuilder.DropColumn(
                name: "codigo_habilitacion",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "causa_externa",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "finalidad",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "grupo_servicios",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "modalidad_atencion",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "servicios",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "valor_total",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "via_ingreso",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "cups_representativo_servicio_id",
                table: "paquetes");

            migrationBuilder.DropColumn(
                name: "correo_facturacion",
                table: "aseguradoras");
        }
    }
}
