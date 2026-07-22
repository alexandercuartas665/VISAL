using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacturacionSnapshotFilaCambio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "facturacion_snapshot_fila_cambios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fila_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_fila = table.Column<int>(type: "integer", nullable: false),
                    columna_original = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor_antes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    valor_despues = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturacion_snapshot_fila_cambios", x => x.id);
                    table.ForeignKey(
                        name: "fk_facturacion_snapshot_fila_cambios_facturacion_snapshots_sna",
                        column: x => x.snapshot_id,
                        principalTable: "facturacion_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_facturacion_snapshot_fila_cambios_snapshot_id_created_at",
                table: "facturacion_snapshot_fila_cambios",
                columns: new[] { "snapshot_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_facturacion_snapshot_fila_cambios_snapshot_id_fila_id_creat",
                table: "facturacion_snapshot_fila_cambios",
                columns: new[] { "snapshot_id", "fila_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "facturacion_snapshot_fila_cambios");
        }
    }
}
