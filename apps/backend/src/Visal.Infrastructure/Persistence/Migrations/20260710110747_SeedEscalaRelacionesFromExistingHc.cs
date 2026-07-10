using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedEscalaRelacionesFromExistingHc : Migration
    {
        // Compatibilidad con datos previos. Antes de esta migracion las escalas
        // se listaban por Tipo=ESCALAS del catalogo global; al migrar el tab a
        // filtrar por relaciones_formulario (tipo ESCALA) las HC existentes
        // quedarian sin escalas hasta que un admin las configure una por una.
        // Este seed genera, para cada tenant, el producto cartesiano
        //   { formatos con Tipo=HISTORIA CLINICA } X { formatos con Tipo=ESCALAS }
        // marcando la relacion tipo ESCALA. ON CONFLICT DO NOTHING respeta
        // las relaciones que el admin ya haya configurado a mano.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO relaciones_formulario
    (id, tenant_id, formulario_origen_id, formulario_destino_id,
     tipo_relacion, activo, observacion, created_at, created_by)
SELECT gen_random_uuid(),
       hc.tenant_id,
       hc.id AS formulario_origen_id,
       es.id AS formulario_destino_id,
       'ESCALA'   AS tipo_relacion,
       TRUE       AS activo,
       'Seed automatico: compatibilidad con datos previos' AS observacion,
       NOW()      AS created_at,
       NULL       AS created_by
FROM form_definitions hc
JOIN form_definitions es
     ON  es.tenant_id = hc.tenant_id
     AND es.activo    = TRUE
     AND UPPER(es.tipo) LIKE '%ESCALA%'
WHERE hc.activo = TRUE
  AND UPPER(hc.tipo) LIKE '%HISTORIA%CLINICA%'
ON CONFLICT (tenant_id, formulario_origen_id, formulario_destino_id) DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Solo removemos las relaciones marcadas por el seed automatico
            // (por observacion). Las que un admin haya creado a mano quedan.
            migrationBuilder.Sql(@"
DELETE FROM relaciones_formulario
WHERE tipo_relacion = 'ESCALA'
  AND observacion   = 'Seed automatico: compatibilidad con datos previos';
");
        }
    }
}
