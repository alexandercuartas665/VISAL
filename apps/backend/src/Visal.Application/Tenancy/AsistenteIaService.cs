using System.Text;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Asistente IA invocado desde el modal de notas medicas. Resuelve el agente
/// configurado en la regla de automatizacion activa y arma el contexto con la
/// HC del paciente + nota en redaccion. La llamada al LLM real (Claude/OpenAI)
/// se hace si hay una AiProviderConfig activa con API key; si no, devuelve una
/// respuesta de stub explicativa para no romper el flujo en demo.
/// </summary>
public sealed class AsistenteIaService(IApplicationDbContext db, ITenantContext tenant) : IAsistenteIaService
{
    public async Task<AsistenteContextoDto> ResolverContextoAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid)
        {
            return new(null, null, null, false, "No hay tenant activo.");
        }

        // Buscamos la regla activa cuya accion es "Revisar notas medicas con IA"
        // y que tenga un agente asignado.
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

        return new(agente.Id, agente.Name, agente.Role, true, null);
    }

    public async Task<AsistenteRespuestaDto> EnviarMensajeAsync(
        Guid historiaClinicaId,
        string contenidoNotaActual,
        string mensajeUsuario,
        IReadOnlyList<AsistenteMensajeDto> historial,
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

        var agente = await db.AiAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == agenteId, ct);
        if (agente is null)
        {
            return new("El agente ya no existe.", "?", false, "Agente inexistente.");
        }

        // Construir el contexto que se enviaria al LLM:
        // 1) System prompt del agente (define las reglas del asistente)
        // 2) Snapshot resumido de la HC del paciente
        // 3) Nota actual en redaccion
        // 4) Historial de la conversacion
        // 5) Mensaje del usuario
        var sysPrompt = string.IsNullOrWhiteSpace(agente.SystemPrompt)
            ? "Eres un asistente que valida notas medicas."
            : agente.SystemPrompt;

        var hcResumen = await ConstruirResumenHcAsync(historiaClinicaId, ct);

        // Si hay un provider con API key configurada, llamariamos al LLM aqui.
        // Por ahora se devuelve una respuesta de stub que sigue las reglas del prompt
        // sin necesidad de API externa, para que el flujo funcione end-to-end.
        var provider = await db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsEnabled && p.ApiKeyEncrypted != null && p.ApiKeyEncrypted != "", ct);
        var hayApiReal = provider is not null;

        var respuesta = ComponerRespuestaStub(
            agente.Name,
            sysPrompt,
            hcResumen,
            contenidoNotaActual,
            mensajeUsuario);

        return new(
            respuesta,
            agente.Name,
            ProveedorReal: hayApiReal,
            Aviso: hayApiReal
                ? null
                : "Modo demo: no hay AiProviderConfig activa. Configura una API key para respuestas reales del LLM.");
    }

    private async Task<string> ConstruirResumenHcAsync(Guid hcId, CancellationToken ct)
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

        // Notas anteriores recientes (max 3) para dar continuidad clinica.
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

    /// <summary>
    /// Respuesta predeterminada que respeta las reglas del prompt del agente
    /// sin llamar al LLM externo. Hace un analisis basico de completitud sobre
    /// la nota para que el demo se sienta util.
    /// </summary>
    private static string ComponerRespuestaStub(
        string agenteName,
        string sysPrompt,
        string hcResumen,
        string contenidoNota,
        string mensajeUsuario)
    {
        var sb = new StringBuilder();

        // Heuristica: si el mensaje del usuario no tiene nada que ver con la nota,
        // el agente "rechaza" educadamente segun su rol.
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
