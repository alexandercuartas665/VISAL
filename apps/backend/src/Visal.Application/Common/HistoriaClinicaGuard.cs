using Microsoft.EntityFrameworkCore;
using Visal.Domain.Entities;

namespace Visal.Application.Common;

/// <summary>
/// Guard de dominio para historias clinicas usado por los servicios de items
/// (medicamentos, insumos, remisiones, RX, laboratorio, servicios propios,
/// incapacidades, certificaciones, escalas, evoluciones, consentimientos).
///
/// Vive aca en Common (no en HistoriaClinicaService) para evitar acoplar los
/// servicios de items al servicio de HC. Cada servicio ya inyecta
/// IApplicationDbContext.
///
/// Regla: una HC cerrada, inactiva o anulada es un documento clinico firmado
/// y NO admite cambios post-cierre. Cualquier mutacion (agregar/editar/eliminar
/// un item de HC) debe llamar EnsureAbiertaAsync ANTES de tocar el DbContext.
/// La violacion se propaga como InvalidOperationException con mensaje
/// humanizado, que la UI ya sabe capturar y mostrar como alerta.
/// </summary>
public static class HistoriaClinicaGuard
{
    /// <summary>Lanza si la HC no existe o no esta en estado "Abierta".</summary>
    public static async Task EnsureAbiertaAsync(
        this IApplicationDbContext db,
        Guid historiaClinicaId,
        CancellationToken ct = default)
    {
        var estado = await db.HistoriasClinicas
            .AsNoTracking()
            .Where(h => h.Id == historiaClinicaId)
            .Select(h => (HistoriaClinicaEstado?)h.Estado)
            .FirstOrDefaultAsync(ct);

        if (estado is null)
        {
            throw new InvalidOperationException(
                "La historia clinica no existe.");
        }
        if (estado.Value != HistoriaClinicaEstado.Abierta)
        {
            var descripcion = estado.Value switch
            {
                HistoriaClinicaEstado.Cerrada => "cerrada",
                HistoriaClinicaEstado.Inactiva => "inactiva",
                _ => estado.Value.ToString().ToLowerInvariant()
            };
            throw new InvalidOperationException(
                $"No se pueden modificar registros: la historia clinica esta {descripcion}.");
        }
    }
}
