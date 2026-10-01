using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy;

public sealed class AtencionOrdenService(IApplicationDbContext db) : IAtencionOrdenService
{
    // Nombre del permiso que exceptua del bloqueo por orden secuencial. Es el
    // mismo que aparece en ModuloCatalogo.Todos y se lee del claim "perms" en
    // el frontend.
    private const string PermisoSaltarOrden = "atencion.saltar-orden";

    public async Task<AtencionOrdenBloqueo?> ValidarAperturaAsync(Guid sesionId, Guid actorUserId, CancellationToken ct = default)
    {
        if (await UsuarioPuedeSaltarOrdenAsync(actorUserId, ct))
        {
            return null;
        }

        // Resolvemos el turno actual y su asignacion. sesionId es el Id del
        // pivote AsignacionTurnoSesion (una fila por turno individual desde
        // task #147, todas con SessionNo=1). El "orden real de sesion" NO vive
        // en SessionNo — vive en la posicion cronologica del AsignacionTurno
        // dentro de su Asignacion (ordenado por CreatedAt asc), que es lo que
        // la UI muestra como "Sesion N" en /atencion.
        var sesion = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => s.Id == sesionId)
            .Select(s => new { s.Id, s.AsignacionTurnoId })
            .FirstOrDefaultAsync(ct);
        if (sesion is null) { return null; }

        var turnoActual = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.Id == sesion.AsignacionTurnoId)
            .Select(t => new { t.Id, t.AsignacionId, t.ProfesionalId })
            .FirstOrDefaultAsync(ct);
        if (turnoActual is null) { return null; }

        // Traer todos los turnos de la misma asignacion (con su profesional) y
        // ordenarlos por el NumeroSesion PERSISTIDO (nace en Coordinacion, no se
        // recomputa) = lo que la UI muestra como "Sesion N". Si algun turno viejo
        // quedara sin backfillear, se cae a la posicion cronologica por CreatedAt/Id
        // como fallback — mismo criterio que la grilla /atencion y Ordenes.
        var turnosRaw = await db.AsignacionTurnos.AsNoTracking()
            .Where(t => t.AsignacionId == turnoActual.AsignacionId)
            .Select(t => new { t.Id, t.CreatedAt, t.ProfesionalId, t.NumeroSesion })
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToListAsync(ct);
        // Numero efectivo por turno: persistido si existe, si no la posicion por
        // CreatedAt/Id (base 1). Reordenamos por ese numero para que la secuencia
        // y los mensajes usen la numeracion que ve el usuario.
        var numEfectivo = new Dictionary<Guid, int>();
        for (int i = 0; i < turnosRaw.Count; i++)
        {
            numEfectivo[turnosRaw[i].Id] = turnosRaw[i].NumeroSesion ?? (i + 1);
        }
        var turnos = turnosRaw
            .OrderBy(t => numEfectivo[t.Id]).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToList();

        int posGlobalActual = numEfectivo[turnoActual.Id];
        if (posGlobalActual <= 1) { return null; }

        // El candado es POR PROFESIONAL: cada profesional lleva su propia secuencia
        // independiente. Solo exigimos que esten completas las sesiones ANTERIORES
        // del MISMO profesional. Ejemplo: si a Juan se le asignan las sesiones 1..10
        // y a Mariano las 11..20, Mariano puede iniciar la 11 sin que Juan cierre la
        // 10, pero NO puede saltar a la 15 sin llevar 11..14. Los numeros del mensaje
        // son los GLOBALES (los que ve el usuario), aunque el control sea por prof.
        var anterioresMismoProf = turnos
            .Where(t => numEfectivo[t.Id] < posGlobalActual)   // turnos antes del actual (numero global asc)
            .Where(t => t.ProfesionalId == turnoActual.ProfesionalId)
            .ToList();
        if (anterioresMismoProf.Count == 0) { return null; }

        var idsAnteriores = anterioresMismoProf.Select(t => t.Id).ToList();

        // "No completados" = turnos que NO tienen pivote AsignacionTurnoSesion, o
        // cuyo pivote esta Completado=false. Cerrar la HC vinculada al turno pone la
        // sesion Completado=true (ver HistoriaClinicaService.Recalcular*).
        var completadosPorTurno = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => idsAnteriores.Contains(s.AsignacionTurnoId))
            .Select(s => new { s.AsignacionTurnoId, s.Completado })
            .ToListAsync(ct);
        var completadoLookup = completadosPorTurno
            .GroupBy(x => x.AsignacionTurnoId)
            .ToDictionary(g => g.Key, g => g.Any(x => x.Completado));

        foreach (var t in anterioresMismoProf)   // en orden global asc
        {
            bool completado = completadoLookup.TryGetValue(t.Id, out var c) && c;
            if (!completado)
            {
                // Numero GLOBAL de la sesion pendiente (la que ve el usuario).
                int posPendienteGlobal = numEfectivo[t.Id];
                // Buscar el pivote existente para incluir su Id en el bloqueo
                // (si aun no existe, dejamos Guid.Empty — el caller usa el mensaje).
                var pivotePendiente = await db.AsignacionTurnoSesiones.AsNoTracking()
                    .Where(s => s.AsignacionTurnoId == t.Id)
                    .Select(s => (Guid?)s.Id)
                    .FirstOrDefaultAsync(ct) ?? Guid.Empty;

                return new AtencionOrdenBloqueo(
                    $"Debes cerrar la sesion {posPendienteGlobal} antes de abrir la sesion {posGlobalActual}.",
                    posPendienteGlobal,
                    turnoActual.AsignacionId,
                    pivotePendiente);
            }
        }

        return null;
    }

    /// <summary>
    /// Solo pasa libre el rol que tenga marcado <c>atencion.saltar-orden</c>
    /// en <c>rol_permisos</c>. Sin exencion por TenantRole: la regla clinica
    /// es universal para el tenant y solo un permiso explicito la releva.
    /// Si el usuario no tiene fila en tenant_users (super admin puro o servicio
    /// sistema) fail-open: no se bloquea; ese caso no proviene del flujo UI.
    /// </summary>
    private async Task<bool> UsuarioPuedeSaltarOrdenAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) { return true; }

        var tenantUser = await db.TenantUsers.AsNoTracking()
            .Where(u => u.PlatformUserId == userId)
            .Select(u => new { u.RolId })
            .FirstOrDefaultAsync(ct);
        if (tenantUser is null) { return true; }

        if (tenantUser.RolId is not Guid rolId) { return false; }

        return await db.RolPermisos.AsNoTracking()
            .AnyAsync(p => p.RolId == rolId && p.Modulo == PermisoSaltarOrden && p.Ver, ct);
    }
}
