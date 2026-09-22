using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAseguradoraInformeContenidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aseguradora_informe_contenidos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    origen = table.Column<int>(type: "integer", nullable: false),
                    tipologia_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    solo_ultimo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aseguradora_informe_contenidos", x => x.id);
                    table.ForeignKey(
                        name: "fk_aseguradora_informe_contenidos_aseguradora_informe_items_it",
                        column: x => x.item_id,
                        principalTable: "aseguradora_informe_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aseguradora_informe_contenidos_item_id",
                table: "aseguradora_informe_contenidos",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_aseguradora_informe_contenidos_tenant_id_item_id_orden",
                table: "aseguradora_informe_contenidos",
                columns: new[] { "tenant_id", "item_id", "orden" });

            // Back-fill: cada archivo/item existente (modelo viejo "1 item = 1
            // origen") pasa a tener un contenido con su origen/tipologia/solo_ultimo.
            // Preserva los datos ya configurados por los tenants.
            migrationBuilder.Sql(@"
                INSERT INTO aseguradora_informe_contenidos
                    (id, item_id, orden, origen, tipologia_archivo_id, solo_ultimo,
                     created_at, tenant_id)
                SELECT
                    gen_random_uuid(), i.id, 0, i.origen, i.tipologia_archivo_id,
                    i.solo_ultimo, now(), i.tenant_id
                FROM aseguradora_informe_items i;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aseguradora_informe_contenidos");
        }
    }
}
