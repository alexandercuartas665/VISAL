using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class OrdenMedicamentoService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IHistoriaPrefillService prefill) : IOrdenMedicamentoService
{
    public async Task<IReadOnlyList<MedicamentoSugerenciaDto>> BuscarSugerenciasAsync(
        string termino, int take = 12, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(termino) || termino.Trim().Length < 2)
        {
            return Array.Empty<MedicamentoSugerenciaDto>();
        }

        var t = termino.Trim().ToLowerInvariant();
        if (take <= 0) { take = 12; }
        if (take > 50) { take = 50; }

        return await db.Medicamentos.AsNoTracking()
            .Where(m =>
                (m.Producto != null && m.Producto.ToLower().Contains(t)) ||
                (m.PrincipioActivo != null && m.PrincipioActivo.ToLower().Contains(t)) ||
                (m.Ium != null && m.Ium.ToLower().Contains(t)) ||
                (m.RegistroSanitario != null && m.RegistroSanitario.ToLower().Contains(t)))
            // Preferir Vigentes / Activos primero
            .OrderBy(m => m.EstadoRegistro == "Vigente" ? 0 : 1)
            .ThenBy(m => m.Producto)
            .Take(take)
            .Select(m => new MedicamentoSugerenciaDto(
                m.Id,
                m.Producto ?? "(sin nombre)",
                m.PrincipioActivo,
                m.Concentracion,
                m.FormaFarmaceutica,
                m.RegistroSanitario,
                m.Ium))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrdenMedicamentoItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == historiaId)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new OrdenMedicamentoItemDto(
                x.Id, x.HistoriaClinicaId, x.MedicamentoId, x.CodigoMedicamento,
                x.NombreMedicamento, x.Cantidad, x.Frecuencia, x.Dias,
                x.Posologia, x.Observacion, x.Orden))
            .ToListAsync(ct);
    }

    public async Task<OrdenMedicamentoItemDto> AgregarAsync(
        Guid historiaId, AgregarMedicamentoRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.NombreMedicamento))
        {
            throw new InvalidOperationException("El nombre del medicamento es obligatorio.");
        }

        // Calcular siguiente orden en la historia.
        var siguiente = 1 + await db.HistoriaClinicaMedicamentos
            .Where(x => x.HistoriaClinicaId == historiaId)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        var entity = new HistoriaClinicaMedicamento
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            MedicamentoId = req.MedicamentoId,
            CodigoMedicamento = Trim(req.CodigoMedicamento),
            NombreMedicamento = req.NombreMedicamento.Trim(),
            Cantidad = Trim(req.Cantidad),
            Frecuencia = Trim(req.Frecuencia),
            Dias = Trim(req.Dias),
            Posologia = Trim(req.Posologia),
            Observacion = Trim(req.Observacion),
            Orden = siguiente
        };
        db.HistoriaClinicaMedicamentos.Add(entity);
        await db.SaveChangesAsync(ct);
        // Refrescar las celdas auto-mapeadas del FormViewer (tabla medicamentos
        // o textarea con lista_numerada) dentro del mismo scope EF Core. Asi el
        // cambio queda persistido atomicamente sin race con el autosave del
        // frontend. Cuando el doctor vuelva al tab Historial, vera las filas.
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new OrdenMedicamentoItemDto(
            entity.Id, entity.HistoriaClinicaId, entity.MedicamentoId, entity.CodigoMedicamento,
            entity.NombreMedicamento, entity.Cantidad, entity.Frecuencia, entity.Dias,
            entity.Posologia, entity.Observacion, entity.Orden);
    }

    public async Task<bool> ActualizarAsync(
        Guid itemId, ActualizarMedicamentoRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaMedicamentos.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        entity.Cantidad = Trim(req.Cantidad);
        entity.Frecuencia = Trim(req.Frecuencia);
        entity.Dias = Trim(req.Dias);
        entity.Posologia = Trim(req.Posologia);
        entity.Observacion = Trim(req.Observacion);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(entity.HistoriaClinicaId, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaMedicamentos.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        db.HistoriaClinicaMedicamentos.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaMedicamentos
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
