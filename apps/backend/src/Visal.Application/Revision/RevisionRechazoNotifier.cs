using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Revision;

/// <summary>
/// Ola 8 RC8c — implementacion best-effort del notificador de rechazo por
/// WhatsApp. Estrategia:
///
///   1. Leer <see cref="RevisionPolicy.NotificarRechazoWhatsApp"/> — si false, salir.
///   2. Cargar HC + su Profesional. Si el profesional no tiene celular, log info.
///   3. Elegir la primera <see cref="WhatsAppLine"/> Connected del tenant. Si no hay, log info.
///   4. Enviar texto plano via <see cref="IWhatsAppConnectorService.SendTestAsync"/>.
///
/// El proposito es que el rechazo no se pierda para el profesional; si el envio
/// falla la trazabilidad va a Serilog y el revisor ve el rechazo confirmado en
/// el kanban de todas formas. La notificacion es informativa, no bloqueante.
/// </summary>
public sealed class RevisionRechazoNotifier : IRevisionRechazoNotifier
{
    private readonly IApplicationDbContext _db;
    private readonly IRevisionPolicyService _policy;
    private readonly IWhatsAppConnectorService _wa;
    private readonly ILogger<RevisionRechazoNotifier> _log;

    public RevisionRechazoNotifier(
        IApplicationDbContext db,
        IRevisionPolicyService policy,
        IWhatsAppConnectorService wa,
        ILogger<RevisionRechazoNotifier> log)
    {
        _db = db;
        _policy = policy;
        _wa = wa;
        _log = log;
    }

    public async Task NotificarAsync(Guid historiaClinicaId, string motivo, Guid actorUserId, CancellationToken ct = default)
    {
        try
        {
            var pol = await _policy.GetAsync(ct);
            if (!pol.NotificarRechazoWhatsApp) { return; }

            var hc = await _db.HistoriasClinicas.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == historiaClinicaId, ct);
            if (hc is null) { return; }

            // El profesional autor: preferimos su celular; si no hay ProfesionalId
            // vinculado a la HC no podemos notificar (por ejemplo si viene solo
            // el EspecialistaNombre denormalizado sin FK).
            if (hc.ProfesionalId is not Guid profId) { return; }

            var prof = await _db.Profesionales.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == profId, ct);
            if (prof is null || string.IsNullOrWhiteSpace(prof.Celular))
            {
                _log.LogInformation("RC8c notificar rechazo hc={HcId} prof={ProfId} sin celular — omitido.",
                    historiaClinicaId, profId);
                return;
            }

            var linea = await _db.WhatsAppLines.AsNoTracking()
                .Where(l => l.Status == WhatsAppLineStatus.Connected)
                .OrderBy(l => l.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (linea is null)
            {
                _log.LogInformation("RC8c notificar rechazo hc={HcId} sin linea WA conectada — omitido.",
                    historiaClinicaId);
                return;
            }

            var pacNombre = await _db.Pacientes.AsNoTracking()
                .Where(p => p.Id == hc.PacienteId)
                .Select(p => p.PrimerNombre + " " + p.PrimerApellido)
                .FirstOrDefaultAsync(ct);

            var texto = ComponerMensaje(hc, motivo, pacNombre);
            var telefono = NormalizarTelefono(prof.Celular);

            var res = await _wa.SendTestAsync(linea.Id, telefono, texto, actorUserId, ct);
            if (!res.Ok)
            {
                _log.LogWarning("RC8c notificar rechazo hc={HcId} linea={LineId} fallo: {Error}",
                    historiaClinicaId, linea.Id, res.Error);
            }
        }
        catch (Exception ex)
        {
            // Nunca lanzar — el rechazo NO se aborta si el WA falla.
            _log.LogWarning(ex, "RC8c notificar rechazo hc={HcId} lanzo excepcion (ignorada).", historiaClinicaId);
        }
    }

    private static string ComponerMensaje(HistoriaClinica hc, string motivo, string? pacienteNombre)
    {
        // Mensaje corto y accionable — no incluye datos clinicos, solo un link
        // deep al modal HC dentro de /atencion. El profesional debe estar
        // autenticado en el navegador para llegar.
        var paciente = string.IsNullOrWhiteSpace(pacienteNombre) ? "el paciente" : pacienteNombre!.Trim();
        var link = $"/atencion?hcId={hc.Id}";
        return $"Tu historia clinica del paciente {paciente} fue devuelta por el revisor.\n" +
               $"Motivo: {motivo}\n" +
               $"Abrir HC: {link}";
    }

    private static string NormalizarTelefono(string raw)
    {
        // El sender espera digitos con codigo de pais. Concatenar en un solo
        // paso simple: quitar todo lo que no sea digito.
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        // Si no viene con codigo pais, asumir Colombia (57).
        if (digits.Length == 10) { digits = "57" + digits; }
        return digits;
    }
}
