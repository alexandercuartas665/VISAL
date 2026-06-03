using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Asistente IA invocado desde el modal de notas medicas. Resuelve el agente
/// configurado en la regla de automatizacion activa y arma el contexto con la
/// HC del paciente + nota en redaccion.
///
/// IMPORTANTE: usa <see cref="IServiceScopeFactory"/> para crear un scope propio
/// en cada operacion en lugar de compartir el <see cref="IApplicationDbContext"/>
/// scoped del circuito Blazor. Esto evita el clasico "second operation was
/// started on this context instance" cuando el chat lateral y el modulo de
/// notas hacen consultas en paralelo durante la apertura del modal.
/// </summary>
public sealed class AsistenteIaService(IServiceScopeFactory scopes, ITenantContext tenant) : IAsistenteIaService
{
    public async Task<AsistenteContextoDto> ResolverContextoAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid)
        {
            return new(null, null, null, false, "No hay tenant activo.");
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var regla = await db.AutomationRules.AsNoTracking()
            .Where(r => r.IsActive
                     && r.Action == AutomationAction.ReviewMedicalNotesWithAi
                     && r.AiAgentId != null)
            .OrderBy(r => r.SortOrder)
            .FirstOrDefaultAsync(ct);

        if (regla is null)
        {
            return new(null, null, null, false,
                "No hay automatizacion activa de 'Revisar notas medicas con IA'. " +
                "Crea o activa una en el modulo Automatizaciones y asignale un agente.");
        }

        var agente = await db.AiAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == regla.AiAgentId, ct);

        if (agente is null)
        {
            return new(regla.AiAgentId, null, null, false,
                "La regla activa apunta a un agente que ya no existe.");
        }
        if (!agente.IsActive)
        {
            return new(agente.Id, agente.Name, agente.Role, false,
                $"El agente '{agente.Name}' esta apagado. Activalo en /agentes.");
        }

        return new(agente.Id, agente.Name, agente.Role, true, null,
            regla.RevisarAlGuardarParcial, regla.RevisarAlGuardarDefinitivo);
    }

    public async Task<AsistenteRespuestaDto> EnviarMensajeAsync(
        Guid pacienteId,
        Guid historiaClinicaId,
        string contenidoNotaActual,
        string mensajeUsuario,
        IReadOnlyList<AsistenteMensajeDto> historial,
        bool persistirMensajeUsuario = true,
        CancellationToken ct = default)
    {
        var ctx = await ResolverContextoAsync(ct);
        if (!ctx.TieneAgente || ctx.AgenteId is not Guid agenteId)
        {
            return new(
                ctx.RazonSinAgente ?? "Asistente no disponible.",
                ctx.AgenteNombre ?? "Sin agente",
                ProveedorReal: false,
                Aviso: ctx.RazonSinAgente);
        }

        if (tenant.TenantId is not Guid tid)
        {
            return new("Sin tenant activo.", ctx.AgenteNombre ?? "?", false, "Sin tenant.");
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var agente = await db.AiAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == agenteId, ct);
        if (agente is null)
        {
            return new("El agente ya no existe.", "?", false, "Agente inexistente.");
        }

        var sysPrompt = string.IsNullOrWhiteSpace(agente.SystemPrompt)
            ? "Eres un asistente que valida notas medicas."
            : agente.SystemPrompt;

        var hcResumen = await ConstruirResumenHcAsync(db, historiaClinicaId, ct);

        var provider = await db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsEnabled && p.ApiKeyEncrypted != null && p.ApiKeyEncrypted != "", ct);
        var hayApiReal = provider is not null;

        var respuesta = ComponerRespuestaStub(
            agente.Name,
            sysPrompt,
            hcResumen,
            contenidoNotaActual,
            mensajeUsuario);

        // Persistir el mensaje del usuario (opcional, se omite en auto-revisiones)
        // y la respuesta del asistente, ambos atados al paciente.
        var ahora = DateTimeOffset.UtcNow;
        if (persistirMensajeUsuario)
        {
            db.AsistenteChatMensajes.Add(new AsistenteChatMensaje
            {
                TenantId = tid,
                PacienteId = pacienteId,
                Rol = "user",
                Texto = mensajeUsuario ?? "",
                Cuando = ahora,
                HistoriaClinicaId = historiaClinicaId,
                AgenteId = agenteId,
                AgenteNombreSnapshot = agente.Name
            });
        }
        db.AsistenteChatMensajes.Add(new AsistenteChatMensaje
        {
            TenantId = tid,
            PacienteId = pacienteId,
            Rol = "assistant",
            Texto = respuesta,
            Cuando = ahora.AddMilliseconds(1), // garantiza orden estable user -> assistant
            HistoriaClinicaId = historiaClinicaId,
            AgenteId = agenteId,
            AgenteNombreSnapshot = agente.Name
        });
        await db.SaveChangesAsync(ct);

        return new(
            respuesta,
            agente.Name,
            ProveedorReal: hayApiReal,
            Aviso: hayApiReal
                ? null
                : "Modo demo: no hay AiProviderConfig activa. Configura una API key para respuestas reales del LLM.");
    }

    public async Task<IReadOnlyList<AsistenteMensajeDto>> ListarHistorialPorPacienteAsync(
        Guid pacienteId, int? limit = null, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var q = db.AsistenteChatMensajes.AsNoTracking()
            .Where(m => m.PacienteId == pacienteId)
            .OrderBy(m => m.Cuando)
            .AsQueryable();
        if (limit is int n && n > 0) { q = q.TakeLast(n).AsQueryable(); }
        return await q
            .Select(m => new AsistenteMensajeDto(m.Rol, m.Texto, m.Cuando))
            .ToListAsync(ct);
    }

    public async Task<AsistenteMensajeDto> AgregarMensajeAsync(
        Guid pacienteId, string rol, string texto,
        Guid? historiaClinicaId = null,
        Guid? notaMedicaId = null,
        Guid? agenteId = null,
        string? agenteNombre = null,
        CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var entity = new AsistenteChatMensaje
        {
            TenantId = tid,
            PacienteId = pacienteId,
            Rol = string.IsNullOrWhiteSpace(rol) ? "system" : rol.Trim(),
            Texto = texto ?? "",
            Cuando = DateTimeOffset.UtcNow,
            HistoriaClinicaId = historiaClinicaId,
            NotaMedicaId = notaMedicaId,
            AgenteId = agenteId,
            AgenteNombreSnapshot = agenteNombre
        };
        db.AsistenteChatMensajes.Add(entity);
        await db.SaveChangesAsync(ct);
        return new AsistenteMensajeDto(entity.Rol, entity.Texto, entity.Cuando);
    }

    public async Task<int> LimpiarHistorialPorPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        return await db.AsistenteChatMensajes
            .Where(m => m.PacienteId == pacienteId)
            .ExecuteDeleteAsync(ct);
    }

    private static async Task<string> ConstruirResumenHcAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId)
            .Select(h => new
            {
                h.Id,
                h.PacienteId,
                h.FechaApertura,
                h.FechaCierre,
                h.Estado,
                h.EspecialistaNombre,
                h.ValoresJson,
                h.FormDefinitionId
            })
            .FirstOrDefaultAsync(ct);
        if (hc is null) { return "(historia clinica no encontrada)"; }

        var paciente = await db.Pacientes.AsNoTracking()
            .Where(p => p.Id == hc.PacienteId)
            .Select(p => new { p.NombreCompleto, p.TipoDocumento, p.NumeroDocumento, p.FechaNacimiento, p.Sexo, p.Edad })
            .FirstOrDefaultAsync(ct);
        var formato = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == hc.FormDefinitionId)
            .Select(f => f.Nombre)
            .FirstOrDefaultAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("== Paciente ==");
        if (paciente is not null)
        {
            sb.Append("Nombre: ").AppendLine(paciente.NombreCompleto);
            sb.Append("Documento: ").Append(paciente.TipoDocumento).Append(' ').AppendLine(paciente.NumeroDocumento);
            sb.Append("Sexo: ").AppendLine(paciente.Sexo ?? "-");
            sb.Append("Edad: ").AppendLine(paciente.Edad?.ToString() ?? "-");
        }
        sb.AppendLine("== Historia Clinica ==");
        sb.Append("Formato: ").AppendLine(formato ?? "-");
        sb.Append("Estado: ").AppendLine(hc.Estado.ToString());
        sb.Append("Especialista: ").AppendLine(hc.EspecialistaNombre ?? "-");

        var notas = await db.NotasMedicas.AsNoTracking()
            .Where(n => n.HistoriaClinicaId == hcId)
            .OrderByDescending(n => n.FechaNota)
            .Take(3)
            .Select(n => new { n.FechaNota, n.Contenido })
            .ToListAsync(ct);
        if (notas.Count > 0)
        {
            sb.AppendLine("== Notas medicas previas (mas recientes primero) ==");
            foreach (var n in notas)
            {
                sb.Append('[').Append(n.FechaNota.ToString("dd/MM/yyyy")).Append("] ");
                sb.AppendLine(Truncate(n.Contenido, 300));
            }
        }
        return sb.ToString();
    }

    private static string ComponerRespuestaStub(
        string agenteName,
        string sysPrompt,
        string hcResumen,
        string contenidoNota,
        string mensajeUsuario)
    {
        var sb = new StringBuilder();

        var msg = (mensajeUsuario ?? "").Trim().ToLowerInvariant();
        var temasInvalidos = new[] {
            "diagnostic", "tratamiento", "que medicamento", "que receta", "que hago", "como curo",
            "que dosis", "es cancer", "es grave", "le doy", "que opinas del paciente"
        };
        if (temasInvalidos.Any(t => msg.Contains(t)))
        {
            sb.Append(agenteName).AppendLine(": Lo siento, mi rol es solo revisar la calidad y completitud de la nota medica.");
            sb.AppendLine("No emito opinion clinica, diagnostico ni recomendaciones de tratamiento.");
            sb.AppendLine("Reformula tu pregunta enfocandola en la nota actual (redaccion, campos, errores).");
            return sb.ToString();
        }

        sb.Append(agenteName).AppendLine(": Revision de la nota actual:");

        var n = (contenidoNota ?? "").Trim();
        if (n.Length == 0)
        {
            sb.AppendLine("- La nota esta VACIA. No puedo evaluar nada todavia.");
            sb.AppendLine("- Cuando escribas, recuerda incluir: motivo de consulta, examen fisico, analisis, plan.");
            return sb.ToString();
        }

        var hallazgos = new List<string>();
        if (n.Length < 60) { hallazgos.Add("La nota es muy corta (menos de 60 caracteres)."); }
        if (!ContieneAlguno(n, "motivo", "consulta", "queja")) { hallazgos.Add("No se identifica el motivo de consulta de forma explicita."); }
        if (!ContieneAlguno(n, "examen", "exploracion", "auscultacion", "palpacion", "inspeccion")) { hallazgos.Add("Falta el examen fisico."); }
        if (!ContieneAlguno(n, "analisis", "impresion diagnostica", "conclusion")) { hallazgos.Add("Falta el analisis o impresion clinica."); }
        if (!ContieneAlguno(n, "plan", "conducta", "recomenda", "indica", "control")) { hallazgos.Add("No se ve un plan/conducta a seguir."); }

        if (hallazgos.Count == 0)
        {
            sb.AppendLine("- La nota tiene los componentes basicos (motivo, examen, analisis, plan).");
            sb.AppendLine("- Calificacion: BUENA.");
        }
        else
        {
            sb.AppendLine("- Observaciones detectadas:");
            foreach (var h in hallazgos) { sb.Append("  * ").AppendLine(h); }
            sb.AppendLine("- Calificacion: " + (hallazgos.Count >= 3 ? "RECHAZADA" : "OBSERVADA") + ".");
        }

        if (!string.IsNullOrWhiteSpace(mensajeUsuario))
        {
            sb.AppendLine();
            sb.Append("Sobre tu pregunta (\"").Append(Truncate(mensajeUsuario, 80)).AppendLine("\"):");
            sb.AppendLine("Respondiendo desde mi rol de supervisor documental, no como medico tratante.");
            sb.AppendLine("Si necesitas validar otro aspecto puntual de la nota, dimelo explicito.");
        }

        return sb.ToString();
    }

    private static bool ContieneAlguno(string texto, params string[] terminos)
    {
        var t = texto.ToLowerInvariant();
        return terminos.Any(x => t.Contains(x));
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "...");
}
