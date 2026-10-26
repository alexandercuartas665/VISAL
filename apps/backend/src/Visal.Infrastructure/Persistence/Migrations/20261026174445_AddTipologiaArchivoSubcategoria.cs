using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTipologiaArchivoSubcategoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "subcategoria",
                table: "nota_medica_documentos",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tipologia_archivo_subcategorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipologia_archivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipologia_archivo_subcategorias", x => x.id);
                    table.ForeignKey(
                        name: "fk_tipologia_archivo_subcategorias_tipologia_archivos_tipologi",
                        column: x => x.tipologia_archivo_id,
                        principalTable: "tipologia_archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tipologia_archivo_subcategorias_tenant_id_tipologia_archivo",
                table: "tipologia_archivo_subcategorias",
                columns: new[] { "tenant_id", "tipologia_archivo_id", "activo" });

            migrationBuilder.CreateIndex(
                name: "ix_tipologia_archivo_subcategorias_tenant_id_tipologia_archivo1",
                table: "tipologia_archivo_subcategorias",
                columns: new[] { "tenant_id", "tipologia_archivo_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tipologia_archivo_subcategorias_tipologia_archivo_id",
                table: "tipologia_archivo_subcategorias",
                column: "tipologia_archivo_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tipologia_archivo_subcategorias");

            migrationBuilder.DropColumn(
                name: "subcategoria",
                table: "nota_medica_documentos");
        }
    }
}
