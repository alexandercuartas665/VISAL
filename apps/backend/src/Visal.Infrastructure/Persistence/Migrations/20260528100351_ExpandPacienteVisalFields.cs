using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPacienteVisalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "barrio",
                table: "pacientes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cie10id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clasificacion_grupo_patologia",
                table: "pacientes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clasificacion_paciente",
                table: "pacientes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo_aceptacion",
                table: "pacientes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contrato1id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contrato2id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contrato3id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "departamento_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "diagnostico_principal",
                table: "pacientes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dias_estancia",
                table: "pacientes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "edad",
                table: "pacientes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estado",
                table: "pacientes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estrato_social",
                table: "pacientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_comentan",
                table: "pacientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_egreso_pad",
                table: "pacientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_ingreso_pad",
                table: "pacientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "grupo_rh",
                table: "pacientes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "incapacidad",
                table: "pacientes",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ips_comenta_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "med_contratado",
                table: "pacientes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "municipio_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "op_ingreso_dias",
                table: "pacientes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pais_origen_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pais_residencia_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sede_atencion_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo_tutela",
                table: "pacientes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo_usuario",
                table: "pacientes",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tutela",
                table: "pacientes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pacientes_sede_atencion_id",
                table: "pacientes",
                column: "sede_atencion_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pacientes_sucursales_sede_atencion_id",
                table: "pacientes",
                column: "sede_atencion_id",
                principalTable: "sucursales",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pacientes_sucursales_sede_atencion_id",
                table: "pacientes");

            migrationBuilder.DropIndex(
                name: "ix_pacientes_sede_atencion_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "barrio",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "cie10id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "clasificacion_grupo_patologia",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "clasificacion_paciente",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "codigo_aceptacion",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "contrato1id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "contrato2id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "contrato3id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "departamento_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "diagnostico_principal",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "dias_estancia",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "edad",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "estado",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "estrato_social",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fecha_comentan",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fecha_egreso_pad",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fecha_ingreso_pad",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "grupo_rh",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "incapacidad",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "ips_comenta_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "med_contratado",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "municipio_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "op_ingreso_dias",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "pais_origen_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "pais_residencia_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "sede_atencion_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "tipo_tutela",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "tipo_usuario",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "tutela",
                table: "pacientes");
        }
    }
}
