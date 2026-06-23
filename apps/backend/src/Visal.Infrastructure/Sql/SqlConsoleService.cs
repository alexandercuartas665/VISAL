using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;

namespace Visal.Infrastructure.Sql;

/// <summary>
/// Implementacion de la consola SQL admin. Ejecuta SQL crudo contra la BD
/// usando el DbConnection del VisalDbContext y registra todo en sql_console_logs.
/// IMPORTANTE: por requerimiento del dueno del producto se permiten DML y DDL
/// sin restricciones. La unica defensa es la auditoria — toda query queda
/// guardada con usuario, tenant y resultado.
/// </summary>
public sealed class SqlConsoleService(VisalDbContext db, ITenantContext tenant) : ISqlConsoleService
{
    private const long QueryTimeoutMs = 30_000;

    public async Task<SqlConsoleExecutionDto> EjecutarAsync(
        string sql, Guid actorUserId, string? actorUserName,
        int rowLimit = 1000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlConsoleExecutionDto(false, "EMPTY", Array.Empty<string>(),
                Array.Empty<IReadOnlyList<string?>>(), null, null, 0, "Query vacia.");
        }

        var queryType = DetectarTipo(sql);
        var sw = Stopwatch.StartNew();
        SqlConsoleExecutionDto result;
        string? errorMessage = null;
        int? rowsAffected = null;
        int? rowsReturned = null;
        var columnas = new List<string>();
        var filas = new List<IReadOnlyList<string?>>();
        var success = false;

        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(ct);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = (int)(QueryTimeoutMs / 1000);

            if (queryType == "SELECT")
            {
                // Lectura: capturamos columnas y filas hasta rowLimit.
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    columnas.Add(reader.GetName(i));
                }
                var count = 0;
                while (await reader.ReadAsync(ct))
                {
                    if (count >= rowLimit) { break; }
                    var fila = new string?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        fila[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
                    }
                    filas.Add(fila);
                    count++;
                }
                rowsReturned = count;
                success = true;
            }
            else
            {
                rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
                success = true;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            success = false;
        }
        finally
        {
            sw.Stop();
        }

        result = new SqlConsoleExecutionDto(
            success, queryType, columnas, filas,
            rowsAffected, rowsReturned, sw.ElapsedMilliseconds, errorMessage);

        // Registrar SIEMPRE en auditoria, exitoso o no. Lo hacemos en una
        // unidad de trabajo separada para no contaminar el estado del
        // contexto despues de un DDL crudo.
        try
        {
            db.SqlConsoleLogs.Add(new SqlConsoleLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = actorUserId == Guid.Empty ? null : actorUserId,
                UserName = string.IsNullOrWhiteSpace(actorUserName) ? null : actorUserName.Trim(),
                Query = sql,
                QueryType = queryType,
                RowsAffected = rowsAffected,
                RowsReturned = rowsReturned,
                DurationMs = sw.ElapsedMilliseconds,
                Success = success,
                ErrorMessage = errorMessage,
                ExecutedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Si la auditoria falla no debe romper la respuesta al usuario.
            // El error original ya esta en `result`.
        }

        return result;
    }

    public async Task<IReadOnlyList<SqlConsoleLogDto>> ListarHistorialAsync(int take = 50, CancellationToken ct = default)
    {
        if (take <= 0) { take = 50; }
        if (take > 500) { take = 500; }
        return await db.SqlConsoleLogs.AsNoTracking()
            .OrderByDescending(x => x.ExecutedAt)
            .Take(take)
            .Select(x => new SqlConsoleLogDto(
                x.Id, x.TenantId, x.UserId, x.UserName, x.Query, x.QueryType,
                x.RowsAffected, x.RowsReturned, x.DurationMs, x.Success,
                x.ErrorMessage, x.ExecutedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SqlTableInfoDto>> ListarTablasAsync(CancellationToken ct = default)
    {
        // pg_stat_user_tables.n_live_tup es la estimacion de filas que el
        // planner usa; mucho mas rapido que COUNT(*) en tablas grandes.
        // Suficiente para el explorador (no requerimos exactitud).
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(ct); }

        var resultado = new List<SqlTableInfoDto>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT relname, COALESCE(n_live_tup, 0) AS filas
                FROM pg_stat_user_tables
                WHERE schemaname = 'public'
                ORDER BY relname;";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var nombre = reader.GetString(0);
                var filas = reader.GetInt64(1);
                var (descripcion, grupo) = ResolverMetadata(nombre);
                resultado.Add(new SqlTableInfoDto(nombre, descripcion, filas, grupo));
            }
        }
        return resultado;
    }

    /// <summary>Mapa estatico de descripciones humanas y grupo logico para cada
    /// tabla conocida. Las tablas no listadas caen al grupo "Otros" con una
    /// descripcion generica. Para agregar tablas nuevas: una linea aqui.</summary>
    private static (string Descripcion, string Grupo) ResolverMetadata(string nombre)
    {
        return nombre switch
        {
            // ---- Plataforma / SaaS ----
            "tenants" => ("Cuenta cliente (agencia/IPS) — una fila por tenant del SaaS.", "Plataforma"),
            "tenant_users" => ("Usuarios pertenecientes a un tenant: doctores, coordinadores, etc.", "Plataforma"),
            "platform_users" => ("Usuarios globales de la plataforma (super admin).", "Plataforma"),
            "tenant_configurations" => ("Configuracion por tenant: vigencia HC, logo, lema, branding.", "Plataforma"),
            "sucursales" => ("Sucursales/sedes operativas del tenant.", "Plataforma"),
            "platform_branding" => ("Branding global de la plataforma (logo de Visal).", "Plataforma"),
            "data_protection_keys" => ("Llaves de encripcion ASP.NET (NO TOCAR).", "Plataforma"),
            "audit_logs" => ("Auditoria general de la plataforma.", "Plataforma"),
            "super_admin_audit_logs" => ("Auditoria de acciones del super admin.", "Plataforma"),
            "sql_console_logs" => ("Auditoria de TODA query ejecutada en esta consola.", "Plataforma"),

            // ---- Catalogos clinicos ----
            "pacientes" => ("Pacientes admitidos en la agencia. Datos demograficos + contactos.", "Pacientes y Catalogos"),
            "catalogos_paciente" => ("Catalogos auxiliares de paciente (estado civil, regimen, etc.).", "Pacientes y Catalogos"),
            "paises" => ("Catalogo de paises (api-colombia).", "Pacientes y Catalogos"),
            "departamentos" => ("Departamentos de Colombia.", "Pacientes y Catalogos"),
            "municipios" => ("Municipios de Colombia.", "Pacientes y Catalogos"),
            "medicamentos" => ("Base CUM/INVIMA — catalogo maestro de medicamentos comerciales.", "Pacientes y Catalogos"),
            "cups" => ("Codigos Unicos de Procedimientos en Salud (CUPS).", "Pacientes y Catalogos"),
            "cie11_configs" => ("Configuracion de la API CIE-11 (WHO).", "Pacientes y Catalogos"),
            "tipologia_archivos" => ("Categorias de archivos adjuntos (firma, RX, etc.).", "Pacientes y Catalogos"),

            // ---- Aseguradoras / contratos ----
            "aseguradoras" => ("EPS/aseguradoras con las que la IPS tiene contrato.", "Contratos"),
            "contratos_aseguradora" => ("Contratos vigentes con cada aseguradora.", "Contratos"),
            "servicios_contrato" => ("Catalogo de servicios prestables bajo cada contrato (tarifas).", "Contratos"),

            // ---- Profesionales ----
            "profesionales" => ("Profesionales asistenciales (medicos, terapeutas, enfermeria).", "Profesionales"),
            "profesional_tipos" => ("Tipos de profesional (catalogo).", "Profesionales"),
            "profesional_subcategorias" => ("Subcategorias dentro de cada tipo de profesional.", "Profesionales"),
            "tenant_user_sucursales" => ("Relacion usuario tenant <-> sucursales habilitadas.", "Profesionales"),

            // ---- Roles y permisos ----
            "roles" => ("Roles del tenant (coordinador, asistencial, admin).", "Roles y Permisos"),
            "rol_permisos" => ("Permisos por rol y modulo.", "Roles y Permisos"),

            // ---- Asignacion y coordinacion ----
            "asignacion_lotes" => ("Lotes de asignacion (cabecera de programacion).", "Asignacion y Turnos"),
            "asignaciones" => ("Asignaciones de servicios a pacientes con cantidades y fechas.", "Asignacion y Turnos"),
            "asignacion_turnos" => ("Turnos individuales generados desde una asignacion.", "Asignacion y Turnos"),
            "asignacion_turno_sesiones" => ("Sesiones reales ejecutadas de cada turno.", "Asignacion y Turnos"),

            // ---- Historia clinica ----
            "form_definitions" => ("Disenos de formularios (HC, escalas, evoluciones, consentimientos).", "Historia Clinica"),
            "historias_clinicas" => ("Cabecera de cada historia clinica abierta por un paciente.", "Historia Clinica"),
            "historia_clinica_medicamentos" => ("Items de medicamentos prescritos en una HC.", "Historia Clinica"),
            "historia_clinica_ordenes_servicio" => ("Items de servicios propios solicitados en la HC.", "Historia Clinica"),
            "historia_clinica_remisiones" => ("Items de remision a especialista en la HC.", "Historia Clinica"),
            "historia_clinica_incapacidades" => ("Items de incapacidad emitidos en la HC.", "Historia Clinica"),
            "historia_clinica_certificaciones" => ("Items de certificacion emitidos en la HC.", "Historia Clinica"),
            "historia_clinica_insumos" => ("Items de insumos consumidos durante la atencion.", "Historia Clinica"),
            "historia_clinica_escalas" => ("Formularios de escalas (Barthel, Morse, etc.) atados a la HC.", "Historia Clinica"),
            "historia_clinica_documentos" => ("Documentos PDF/imagen anexos a la HC.", "Historia Clinica"),
            "relaciones_formulario" => ("Que escalas/evoluciones/consentimientos aplican a cada HC.", "Historia Clinica"),

            // ---- Notas medicas ----
            "notas_medicas" => ("Notas medicas/seguimiento del paciente entre HCs.", "Notas Medicas"),
            "nota_medica_documentos" => ("Adjuntos a las notas medicas.", "Notas Medicas"),
            "firma_paciente_requests" => ("Solicitudes de firma remota del paciente via WhatsApp.", "Notas Medicas"),

            // ---- Interoperabilidad RDA / MinSalud ----
            "rda_eventos" => ("Eventos RDA generados/enviados al sandbox de MinSalud.", "Interoperabilidad"),
            "interoperabilidad_configs" => ("Configuracion endpoints MinSalud (singleton por tenant).", "Interoperabilidad"),
            "interoperabilidad_credenciales_sede" => ("Credenciales OAuth IHCE por sede x ambiente.", "Interoperabilidad"),

            // ---- IA / Agentes ----
            "ai_agents" => ("Agentes IA configurados (clasificador, copiloto, monitor de notas).", "IA y Automatizacion"),
            "ai_agent_prompts" => ("Versiones de prompts por agente.", "IA y Automatizacion"),
            "ai_provider_configs" => ("Configuracion proveedores IA (OpenAI, Anthropic, etc.).", "IA y Automatizacion"),
            "ai_usage_logs" => ("Tokens consumidos y costo por ejecucion IA.", "IA y Automatizacion"),
            "automation_rules" => ("Reglas de automatizacion (trigger -> accion).", "IA y Automatizacion"),
            "asistente_chat_mensajes" => ("Historial del chat IA Asistente de Notas por paciente.", "IA y Automatizacion"),

            // ---- WhatsApp / mensajeria ----
            "whats_app_lines" => ("Lineas/numeros WhatsApp activos del tenant.", "WhatsApp"),
            "whats_app_messages" => ("Historial de mensajes WhatsApp del tenant.", "WhatsApp"),
            "whats_app_contacts" => ("Contactos/destinatarios WhatsApp.", "WhatsApp"),
            "evolution_master_configs" => ("Configuracion master del servidor Evolution API.", "WhatsApp"),
            "tenant_evolution_configs" => ("Configuracion Evolution por tenant.", "WhatsApp"),
            "message_templates" => ("Plantillas pre-grabadas de mensajes.", "WhatsApp"),
            "message_template_media" => ("Adjuntos de plantillas de mensaje.", "WhatsApp"),
            "webhook_configs" => ("Webhooks configurados para integraciones externas.", "WhatsApp"),

            // ---- Billing / Wompi ----
            "subscriptions" => ("Suscripciones SaaS de cada tenant.", "Billing"),
            "plans" => ("Planes/tarifas comerciales del SaaS.", "Billing"),
            "payments" => ("Pagos procesados.", "Billing"),
            "payment_receipts" => ("Comprobantes PDF de pagos.", "Billing"),
            "wompi_master_configs" => ("Configuracion master de Wompi.", "Billing"),
            "wompi_webhook_events" => ("Eventos webhook recibidos de Wompi (idempotencia).", "Billing"),

            _ => ("(Sin descripcion registrada — usa la tabla a tu criterio.)", "Otros")
        };
    }

    /// <summary>Toma la primera palabra significativa para clasificar la query.
    /// Sirve para auditoria y para decidir SELECT (reader) vs DML/DDL (ExecuteNonQuery).</summary>
    private static string DetectarTipo(string sql)
    {
        var trimmed = sql.TrimStart();
        // Saltar comentarios SQL de linea (-- ...) al inicio.
        while (trimmed.StartsWith("--"))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl < 0) { trimmed = ""; break; }
            trimmed = trimmed[(nl + 1)..].TrimStart();
        }
        var space = trimmed.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '(' });
        var first = (space < 0 ? trimmed : trimmed[..space]).ToUpperInvariant();
        return first switch
        {
            "SELECT" or "WITH" or "TABLE" or "VALUES" => "SELECT",
            "INSERT" => "INSERT",
            "UPDATE" => "UPDATE",
            "DELETE" => "DELETE",
            "CREATE" or "ALTER" or "DROP" or "TRUNCATE" or "GRANT" or "REVOKE" => "DDL",
            _ => "OTHER"
        };
    }
}
