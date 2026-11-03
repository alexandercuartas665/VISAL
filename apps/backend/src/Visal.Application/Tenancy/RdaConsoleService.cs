using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Lecturas y acciones de mantenimiento sobre la tabla rda_eventos. NO construye Bundles
/// (eso es del RdaBuilderService) ni envia (eso sera del IhceSenderService en Ola 5).
/// </summary>
public sealed class RdaConsoleService(
    IApplicationDbContext db,
    ISecretProtector secrets,
    ILogger<RdaConsoleService> log) : IRdaConsoleService
{
    public async Task<IReadOnlyList<RdaEventoRowDto>> ListarAsync(RdaConsoleFiltro filtro, CancellationToken ct = default)
    {
        // Join contra paciente, profesional y sucursal para sacar nombres legibles.
        // Ademas hacemos un LEFT JOIN a interoperabilidad_credenciales_sede para
        // saber POR FILA si (SucursalId, Ambiente) del evento tiene credencial usable
        // (ClientId + ClientSecret no vacios) — asi la UI puede deshabilitar "Enviar al IHCE"
        // antes de que el usuario lo clickee y lance excepcion tardia en el sender.
        var q = from e in db.RdaEventos.AsNoTracking()
                join p in db.Pacientes.AsNoTracking() on e.PacienteId equals p.Id
                join s in db.Sucursales.AsNoTracking() on e.SucursalId equals s.Id
                join pr in db.Profesionales.AsNoTracking() on e.ProfesionalId equals pr.Id into prGroup
                from pr in prGroup.DefaultIfEmpty()
                join cr in db.InteroperabilidadCredencialesSede.AsNoTracking()
                    on new { Sid = e.SucursalId, Amb = e.Ambiente } equals new { Sid = cr.SucursalId, Amb = cr.Ambiente } into crGroup
                from cr in crGroup.DefaultIfEmpty()
                select new
                {
                    e.Id,
                    e.FechaGeneracion,
                    PacienteNombre = p.NombreCompleto,
                    PacienteDocumento = p.NumeroDocumento,
                    ProfesionalNombre = pr != null ? pr.NombreCompleto : "(sin firmante)",
                    e.SucursalId,
                    SucursalNombre = s.Nombre,
                    // Servicio de la asignacion asociada a la HC (via pivote). Subconsulta
                    // correlacionada: EF la traduce a un scalar subquery por fila.
                    Servicio = (from pv in db.AsignacionTurnoSesionHcs.AsNoTracking()
                                join se in db.AsignacionTurnoSesiones.AsNoTracking() on pv.SesionId equals se.Id
                                join tu in db.AsignacionTurnos.AsNoTracking() on se.AsignacionTurnoId equals tu.Id
                                join asig in db.Asignaciones.AsNoTracking() on tu.AsignacionId equals asig.Id
                                where pv.HistoriaClinicaId == e.HistoriaClinicaId
                                select asig.NombreServicio).FirstOrDefault(),
                    e.Modalidad,
                    e.Ambiente,
                    e.Estado,
                    e.Intentos,
                    e.UltimoIntento,
                    e.FechaEnvio,
                    e.ReferenciaMinsalud,
                    e.BundleHash,
                    e.TipoRda,
                    e.HistoriaClinicaId,
                    TieneCredencialSede = cr != null
                        && !string.IsNullOrEmpty(cr.ClientId)
                        && !string.IsNullOrEmpty(cr.ClientSecretCifrado)
                };

        if (!string.IsNullOrWhiteSpace(filtro.Documento))
        {
            var doc = filtro.Documento.Trim();
            q = q.Where(x => x.PacienteDocumento.Contains(doc));
        }
        if (!string.IsNullOrWhiteSpace(filtro.Paciente))
        {
            var pac = filtro.Paciente.Trim();
            q = q.Where(x => x.PacienteNombre.Contains(pac) || x.PacienteDocumento.Contains(pac));
        }
        if (filtro.SucursalId is Guid sedeId && sedeId != Guid.Empty)
        {
            q = q.Where(x => x.SucursalId == sedeId);
        }
        if (!string.IsNullOrWhiteSpace(filtro.Profesional))
        {
            var prof = filtro.Profesional.Trim();
            q = q.Where(x => x.ProfesionalNombre.Contains(prof));
        }
        if (!string.IsNullOrWhiteSpace(filtro.Servicio))
        {
            var serv = filtro.Servicio.Trim();
            q = q.Where(x => x.Servicio != null && x.Servicio.Contains(serv));
        }
        if (filtro.IntentosMin is int minInt && minInt > 0)
        {
            q = q.Where(x => x.Intentos >= minInt);
        }
        if (filtro.SoloRechazados)
        {
            q = q.Where(x => x.Estado == EstadoRdaEvento.Rechazado);
        }
        if (filtro.Estado is EstadoRdaEvento est) { q = q.Where(x => x.Estado == est); }
        if (filtro.Ambiente is AmbienteIhce amb) { q = q.Where(x => x.Ambiente == amb); }
        if (filtro.Desde is DateOnly desde)
        {
            var dt = desde.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(x => x.FechaGeneracion >= dt);
        }
        if (filtro.Hasta is DateOnly hasta)
        {
            var dt = hasta.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            q = q.Where(x => x.FechaGeneracion <= dt);
        }
        if (filtro.SoloVigentes)
        {
            // Dedup al evento vigente de cada (HistoriaClinica, Tipo): deja fuera toda fila que
            // tenga OTRO evento mas reciente del mismo caso. Asi la parrilla muestra el estado
            // actual por caso (y el filtro de estado/SoloRechazados actua sobre ese vigente), en
            // vez de acumular reintentos duplicados y rechazos ya superados. El historial completo
            // sigue disponible por fila (modal) y apagando este flag. El filtro de tenant de EF
            // aplica tambien a la subconsulta.
            q = q.Where(x => !db.RdaEventos.AsNoTracking().Any(e2 =>
                e2.HistoriaClinicaId == x.HistoriaClinicaId
                && e2.TipoRda == x.TipoRda
                && e2.FechaGeneracion > x.FechaGeneracion));
        }

        var rows = await q.OrderByDescending(x => x.FechaGeneracion).Take(500).ToListAsync(ct);
        return rows.Select(r => new RdaEventoRowDto(
            r.Id, r.FechaGeneracion, r.PacienteNombre, r.PacienteDocumento,
            r.ProfesionalNombre, r.SucursalNombre, r.Servicio, r.Modalidad, r.Ambiente,
            r.Estado, r.Intentos, r.UltimoIntento, r.FechaEnvio, r.ReferenciaMinsalud, r.BundleHash,
            r.TipoRda,
            r.TieneCredencialSede,
            r.TieneCredencialSede
                ? null
                : $"La sede {r.SucursalNombre} no tiene credenciales configuradas para el ambiente {r.Ambiente}. Configuralas en Configuracion de Interoperabilidad.",
            r.HistoriaClinicaId
            )).ToList();
    }

    public async Task<IReadOnlyList<RdaEventoHistorialItemDto>> ListarHistorialAsync(
        Guid id, CancellationToken ct = default)
    {
        // El caso lo define (HistoriaClinica, Tipo). Resolvemos ese par desde el evento clickeado
        // y traemos TODOS sus eventos del mas reciente al mas viejo. El vigente es el primero.
        var clave = await db.RdaEventos.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { e.HistoriaClinicaId, e.TipoRda })
            .FirstOrDefaultAsync(ct);
        if (clave is null) { return Array.Empty<RdaEventoHistorialItemDto>(); }

        var eventos = await db.RdaEventos.AsNoTracking()
            .Where(e => e.HistoriaClinicaId == clave.HistoriaClinicaId && e.TipoRda == clave.TipoRda)
            .OrderByDescending(e => e.FechaGeneracion)
            .Select(e => new
            {
                e.Id, e.FechaGeneracion, e.Estado, e.Intentos, e.UltimoIntento, e.BundleHash, e.ErroresJson
            })
            .ToListAsync(ct);

        return eventos.Select((e, idx) => new RdaEventoHistorialItemDto(
            e.Id, e.FechaGeneracion, e.Estado, e.Intentos, e.UltimoIntento, e.BundleHash,
            ExtraerCodigoError(e.ErroresJson),
            EsVigente: idx == 0)).ToList();
    }

    /// <summary>Extrae el code MinSalud del primer issue de un errores_json
    /// ({"body":"&lt;OperationOutcome como string&gt;",...}). Best-effort: null si no parsea.</summary>
    private static string? ExtraerCodigoError(string? erroresJson)
    {
        if (string.IsNullOrWhiteSpace(erroresJson)) { return null; }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(erroresJson);
            if (!doc.RootElement.TryGetProperty("body", out var bodyEl)) { return null; }
            var body = bodyEl.ValueKind == System.Text.Json.JsonValueKind.String ? bodyEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(body) || body![0] != '{') { return null; }
            using var oo = System.Text.Json.JsonDocument.Parse(body);
            if (!oo.RootElement.TryGetProperty("issue", out var issues)
                || issues.ValueKind != System.Text.Json.JsonValueKind.Array
                || issues.GetArrayLength() == 0)
            {
                return null;
            }
            var issue0 = issues[0];
            if (issue0.TryGetProperty("details", out var det)
                && det.TryGetProperty("coding", out var coding)
                && coding.ValueKind == System.Text.Json.JsonValueKind.Array
                && coding.GetArrayLength() > 0
                && coding[0].TryGetProperty("code", out var codeEl))
            {
                return codeEl.GetString();
            }
            return issue0.TryGetProperty("code", out var ic) ? ic.GetString() : null;
        }
        catch { return null; }
    }

    public async Task<RdaEventoDetailDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.RdaEventos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return null; }
        return new RdaEventoDetailDto(e.Id, e.BundleJson, e.BundleHash, e.Estado,
            e.Intentos, e.ErroresJson, e.RespuestaMinsalud, e.ReferenciaMinsalud, e.FechaGeneracion, e.FechaEnvio);
    }

    public async Task<RdaEventoCredencialesDto?> ObtenerCredencialesUsadasAsync(Guid id, CancellationToken ct = default)
    {
        // Devuelve las credenciales EXACTAS que uso (o usaria) este envio. Descifra
        // ClientSecret y APIMsubskey con ISecretProtector — se hace on-demand y NO se
        // persiste en logs (los strings sensibles nunca se pasan a log.Log*).
        //
        // Uso: al abrir un ticket con MinSalud, el operador necesita mostrar los
        // valores exactos que emitio el envio fallido, sin tener que sacarlos de la
        // BD a mano ni desde el codigo. La UI enmascara por defecto con toggle.
        var e = await db.RdaEventos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return null; }

        var cfg = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var cred = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SucursalId == e.SucursalId && c.Ambiente == e.Ambiente, ct);

        string? clientSecret = null;
        try
        {
            if (cred is not null && !string.IsNullOrEmpty(cred.ClientSecretCifrado))
            {
                clientSecret = secrets.Unprotect(cred.ClientSecretCifrado);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Fallo Unprotect ClientSecret evento {Id}", id);
        }

        string? apimSubskey = null;
        try
        {
            var cifrado = e.Ambiente == AmbienteIhce.Produccion
                ? cfg?.ApimSubskeyProduccionCifrada
                : cfg?.ApimSubskeySandboxCifrada;
            if (!string.IsNullOrEmpty(cifrado))
            {
                apimSubskey = secrets.Unprotect(cifrado);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Fallo Unprotect ApimSubskey evento {Id}", id);
        }

        var endpointBase = e.Ambiente == AmbienteIhce.Produccion ? cfg?.EndpointProduccion : cfg?.EndpointSandbox;
        var pathEnvio = e.TipoRda == TipoRdaIhce.Consulta ? cfg?.PathEnvioRdaConsulta : cfg?.PathEnvioRda;

        return new RdaEventoCredencialesDto(
            CodigoHabilitacion: cred?.CodigoHabilitacion,
            ClientId: cred?.ClientId,
            ClientSecret: clientSecret,
            ApimSubskey: apimSubskey,
            AzureTenantId: cfg?.AzureTenantId,
            Scope: cfg?.Scope,
            EndpointBase: endpointBase,
            PathEnvio: pathEnvio,
            Ambiente: e.Ambiente.ToString());
    }

    public async Task<IReadOnlyList<HcCandidataRdaDto>> ListarHcCandidatasAsync(string? buscar, CancellationToken ct = default)
    {
        var q = from h in db.HistoriasClinicas.AsNoTracking()
                join p in db.Pacientes.AsNoTracking() on h.PacienteId equals p.Id
                join fd in db.FormDefinitions.AsNoTracking() on h.FormDefinitionId equals fd.Id into fdg
                from fd in fdg.DefaultIfEmpty()
                select new
                {
                    h.Id,
                    PacienteNombre = p.NombreCompleto,
                    PacienteDocumento = p.NumeroDocumento,
                    h.FechaApertura,
                    h.FechaCierre,
                    FormatoCodigo = fd != null ? fd.Codigo : null,
                    Estado = h.Estado
                };
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim();
            q = q.Where(x => x.PacienteNombre.Contains(b) || x.PacienteDocumento.Contains(b));
        }
        var rows = await q.OrderByDescending(x => x.FechaApertura).Take(50).ToListAsync(ct);
        return rows.Select(r => new HcCandidataRdaDto(r.Id, r.PacienteNombre, r.PacienteDocumento,
            r.FechaApertura, r.FechaCierre, r.FormatoCodigo, r.Estado.ToString())).ToList();
    }

    public async Task<IReadOnlyList<RdaReprocesoItemDto>> ListarPendientesReprocesoAsync(CancellationToken ct = default)
    {
        // Todos los eventos del tenant que NO estan Aceptado, EXCLUYENDO los de una HC+Tipo
        // que ya tiene un Aceptado (no reabrir lo confirmado). El LEFT JOIN a credenciales
        // es el mismo de ListarAsync: sin credencial de sede no se puede enviar.
        var q = from e in db.RdaEventos.AsNoTracking()
                join p in db.Pacientes.AsNoTracking() on e.PacienteId equals p.Id
                join cr in db.InteroperabilidadCredencialesSede.AsNoTracking()
                    on new { Sid = e.SucursalId, Amb = e.Ambiente } equals new { Sid = cr.SucursalId, Amb = cr.Ambiente } into crGroup
                from cr in crGroup.DefaultIfEmpty()
                where e.Estado != EstadoRdaEvento.Aceptado
                    && !db.RdaEventos.Any(x => x.HistoriaClinicaId == e.HistoriaClinicaId
                        && x.TipoRda == e.TipoRda
                        && x.Estado == EstadoRdaEvento.Aceptado)
                select new
                {
                    e.Id,
                    e.HistoriaClinicaId,
                    e.TipoRda,
                    e.Modalidad,
                    e.Estado,
                    e.FechaGeneracion,
                    PacienteNombre = p.NombreCompleto,
                    PacienteDocumento = p.NumeroDocumento,
                    TieneCredencialSede = cr != null
                        && !string.IsNullOrEmpty(cr.ClientId)
                        && !string.IsNullOrEmpty(cr.ClientSecretCifrado)
                };

        var rows = await q.ToListAsync(ct);

        // Dedup por (HC, Tipo): quedarse con el evento mas reciente (si hay varios intentos
        // fallidos de la misma HC, regenerar desde la HC una sola vez ya los cubre a todos).
        var dedup = rows
            .GroupBy(r => new { r.HistoriaClinicaId, r.TipoRda })
            .Select(g => g.OrderByDescending(x => x.FechaGeneracion).First())
            .OrderBy(r => r.FechaGeneracion)
            .Select(r => new RdaReprocesoItemDto(
                r.Id, r.HistoriaClinicaId, r.TipoRda, r.Modalidad, r.Estado,
                r.PacienteNombre, r.PacienteDocumento, r.TieneCredencialSede))
            .ToList();
        return dedup;
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.RdaEventos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado != EstadoRdaEvento.Borrador)
        {
            // Auditoria inmutable: una vez validado / enviado / aceptado no se borra.
            throw new InvalidOperationException(
                $"No se puede eliminar el evento {id}: estado {e.Estado}. Solo se permiten borrar Borradores.");
        }
        db.RdaEventos.Remove(e);
        await db.SaveChangesAsync(ct);
        log.LogInformation("RdaEvento {Id} borrado por {Actor}", id, actor);
        return true;
    }
}
