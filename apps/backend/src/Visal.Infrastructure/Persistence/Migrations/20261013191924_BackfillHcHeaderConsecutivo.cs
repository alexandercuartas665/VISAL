using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillHcHeaderConsecutivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill del "HC N°" guardado en el encabezado del formulario de cada historia.
            // Antes HeaderAutoFillHelper escribia el prefijo de 8 hex del UUID v7 (primeros 8;
            // una version aun mas vieja usaba los ultimos 8). Esos prefijos COLISIONAN entre HCs
            // creadas en el mismo milisegundo y devolvian duplicados al buscar por ese numero.
            // Reemplazamos, SOLO en las claves de encabezado (hdr:*) cuyo valor sea exactamente
            // el hex de 8 (first-8 o last-8) de esa HC, por el consecutivo publico HC-000123
            // (= CodigoPublico.Hc: "HC-" + consecutivo con 6 digitos). Idempotente: tras correr,
            // el valor ya es "HC-..." y deja de matchear, asi que re-ejecutar no hace nada.
            // Solo toca filas afectadas; preserva todas las demas claves/valores tal cual.
            migrationBuilder.Sql(@"
                UPDATE historias_clinicas h
                SET valores_json = (
                    SELECT jsonb_object_agg(
                        kv.key,
                        CASE
                            WHEN kv.key LIKE 'hdr:%'
                             AND upper(kv.value) IN (
                                   upper(substr(replace(h.id::text,'-',''),1,8)),
                                   upper(right(replace(h.id::text,'-',''),8)))
                            THEN to_jsonb('HC-' || lpad(h.consecutivo::text, 6, '0'))
                            ELSE h.valores_json -> kv.key
                        END)
                    FROM jsonb_each_text(h.valores_json) kv
                )
                WHERE h.valores_json IS NOT NULL
                  AND h.consecutivo IS NOT NULL
                  AND EXISTS (
                        SELECT 1 FROM jsonb_each_text(h.valores_json) kv2
                        WHERE kv2.key LIKE 'hdr:%'
                          AND upper(kv2.value) IN (
                                upper(substr(replace(h.id::text,'-',''),1,8)),
                                upper(right(replace(h.id::text,'-',''),8)))
                  );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill de datos irreversible: no reconstruimos el hex (se perdio cual era el
            // prefijo original). El valor HC-000xxx es el correcto; no hay rollback de datos.
        }
    }
}
