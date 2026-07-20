using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Ola 8 RC8a — Traslada los CSVs de allow-list del REVISOR CLINICO IA que Ola
    /// 5c habia sembrado en <c>ai_agent_prompts.body</c> (name = "revision.allowed_tools")
    /// a la columna dedicada <c>ai_agents.allowed_tools_csv</c> (introducida en Ola 6 RC6d).
    /// Elimina las filas legacy tras copiarlas. Idempotente: si un agente ya tiene la
    /// columna seteada, no la sobrescribe. Down no reconstruye las filas legacy porque
    /// el fallback en el orquestador fue eliminado en la misma ola.
    /// </summary>
    public partial class MigrateAllowedToolsSeedsToColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Copia el body al AllowedToolsCsv del agente cuando aun no esta seteado.
            migrationBuilder.Sql(@"
                UPDATE ai_agents a
                SET allowed_tools_csv = p.body
                FROM ai_agent_prompts p
                WHERE p.agent_id = a.id
                  AND p.name = 'revision.allowed_tools'
                  AND (a.allowed_tools_csv IS NULL OR btrim(a.allowed_tools_csv) = '');
            ");

            // Borra los prompts legacy para que no queden datos huerfanos y para
            // evitar confusion futura sobre cual es la fuente autoritativa.
            migrationBuilder.Sql(@"
                DELETE FROM ai_agent_prompts
                WHERE name = 'revision.allowed_tools';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No se reconstruyen las filas legacy: el fallback fue eliminado en Ola 8.
        }
    }
}
