using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy;

/// <summary>
/// Borrado seguro de los artefactos clinicos colgados de un conjunto de historias
/// clinicas. Marca para borrado (RemoveRange, rastreado) los hijos RESTRICT de la HC
/// (revisiones + eventos, eventos RDA, ordenes de medicamentos publicas), el pivote
/// sesion-HC y la propia HC. NO llama a SaveChanges: el caller compone la transaccion,
/// y EF ordena los DELETE respetando las FK (el pivote se borra antes que la sesion).
/// La cascada de BD de la HC (notas, formularios, ordenes de la HC, etc.) se dispara
/// al ejecutarse el DELETE de la HC.
///
/// Compartido por el borrado de coordinacion (<see cref="AsignacionService"/>) y por la
/// cancelacion de cita de Agendas (<c>AsignacionAgendasService</c>) para que ambos
/// limpien igual. Refuerzo 5: con la FK <c>asignacion_turno_sesion_hcs.sesion_id</c> en
/// <c>ON DELETE RESTRICT</c>, el pivote SIEMPRE debe borrarse explicitamente antes que la
/// sesion; esta rutina lo garantiza (ya no se depende del CASCADE que antes orfanaba la HC).
/// </summary>
internal static class ArtefactosClinicosCleanup
{
    public static async Task BorrarHcsAsync(
        IApplicationDbContext db, IReadOnlyList<Guid> hcIds, CancellationToken ct)
    {
        if (hcIds is null || hcIds.Count == 0) { return; }

        // Hijos RESTRICT de la HC: revisiones (con sus eventos), eventos RDA y ordenes de
        // medicamentos publicas. Hay que marcarlos antes que la HC (EF los ordena primero).
        var revs = await db.RevisionesClinica
            .Where(r => hcIds.Contains(r.HistoriaClinicaId))
            .ToListAsync(ct);
        if (revs.Count > 0)
        {
            var revIds = revs.Select(r => r.Id).ToList();
            var eventos = await db.RevisionClinicaEventos
                .Where(e => revIds.Contains(e.RevisionClinicaId))
                .ToListAsync(ct);
            db.RevisionClinicaEventos.RemoveRange(eventos);
            db.RevisionesClinica.RemoveRange(revs);
        }

        var rda = await db.RdaEventos
            .Where(e => hcIds.Contains(e.HistoriaClinicaId))
            .ToListAsync(ct);
        db.RdaEventos.RemoveRange(rda);

        var ordenes = await db.OrdenesMedicamentosPublicas
            .Where(o => hcIds.Contains(o.HistoriaClinicaId))
            .ToListAsync(ct);
        db.OrdenesMedicamentosPublicas.RemoveRange(ordenes);

        // Pivote sesion-HC: SIEMPRE antes de la sesion (FK RESTRICT) y antes de la HC.
        var pivotes = await db.AsignacionTurnoSesionHcs
            .Where(pv => hcIds.Contains(pv.HistoriaClinicaId))
            .ToListAsync(ct);
        db.AsignacionTurnoSesionHcs.RemoveRange(pivotes);

        // La HC (su cascada de BD arrastra notas/formularios/ordenes/etc.).
        var hcs = await db.HistoriasClinicas
            .Where(h => hcIds.Contains(h.Id))
            .ToListAsync(ct);
        db.HistoriasClinicas.RemoveRange(hcs);
    }
}
