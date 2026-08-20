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
            .OrderBy(x => x.NumeroOrden)
            .ThenBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new OrdenMedicamentoItemDto(
                x.Id, x.HistoriaClinicaId, x.MedicamentoId, x.CodigoMedicamento,
                x.NombreMedicamento, x.Cantidad, x.Frecuencia, x.Dias,
                x.Posologia, x.Observacion, x.Orden, x.MipresUrl, x.NumeroOrden))
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
        // Guard: HC debe estar Abierta. Una HC cerrada es un documento firmado.
        await db.EnsureAbiertaAsync(historiaId, ct);
        var numeroOrden = req.NumeroOrden <= 0 ? 1 : req.NumeroOrden;
        // Si esa orden ya fue emitida (QR firmado), editar la anula: revocamos su
        // emision vigente para que la impresion anterior no quede desalineada. Al
        // reimprimir se re-emite un QR nuevo con los medicamentos actualizados.
        await RevocarEmisionMedActivaAsync(historiaId, numeroOrden, actor, ct);

        // Calcular siguiente posicion DENTRO de esa orden (grupo).
        var siguiente = 1 + await db.HistoriaClinicaMedicamentos
            .Where(x => x.HistoriaClinicaId == historiaId && x.NumeroOrden == numeroOrden)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        var entity = new HistoriaClinicaMedicamento
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            NumeroOrden = numeroOrden,
            MedicamentoId = req.MedicamentoId,
            CodigoMedicamento = Trim(req.CodigoMedicamento),
            NombreMedicamento = req.NombreMedicamento.Trim(),
            Cantidad = Trim(req.Cantidad),
            Frecuencia = Trim(req.Frecuencia),
            Dias = Trim(req.Dias),
            Posologia = Trim(req.Posologia),
            Observacion = Trim(req.Observacion),
            MipresUrl = Trim(req.MipresUrl),
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
            entity.Posologia, entity.Observacion, entity.Orden, entity.MipresUrl, entity.NumeroOrden);
    }

    public async Task<bool> ActualizarAsync(
        Guid itemId, ActualizarMedicamentoRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaMedicamentos.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        await db.EnsureAbiertaAsync(entity.HistoriaClinicaId, ct);
        await RevocarEmisionMedActivaAsync(entity.HistoriaClinicaId, entity.NumeroOrden, actor, ct);
        entity.Cantidad = Trim(req.Cantidad);
        entity.Frecuencia = Trim(req.Frecuencia);
        entity.Dias = Trim(req.Dias);
        entity.Posologia = Trim(req.Posologia);
        entity.Observacion = Trim(req.Observacion);
        if (req.MipresUrl is not null) { entity.MipresUrl = Trim(req.MipresUrl); }
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(entity.HistoriaClinicaId, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaMedicamentos.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        await db.EnsureAbiertaAsync(hcId, ct);
        await RevocarEmisionMedActivaAsync(hcId, entity.NumeroOrden, actor, ct);
        db.HistoriaClinicaMedicamentos.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<bool> EliminarOrdenAsync(Guid historiaId, int numeroOrden, Guid actor, CancellationToken ct = default)
    {
        await db.EnsureAbiertaAsync(historiaId, ct);
        var items = await db.HistoriaClinicaMedicamentos
            .Where(x => x.HistoriaClinicaId == historiaId && x.NumeroOrden == numeroOrden)
            .ToListAsync(ct);
        if (items.Count == 0) { return false; }
        await RevocarEmisionMedActivaAsync(historiaId, numeroOrden, actor, ct);
        db.HistoriaClinicaMedicamentos.RemoveRange(items);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(historiaId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaMedicamentos
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }

    /// <summary>
    /// Al editar los medicamentos de una HC ABIERTA, la orden emitida (QR firmado)
    /// deja de ser fiel a lo que se prescribe. En vez de bloquear, revocamos la
    /// emision vigente (baja logica) y dejamos editar: la orden impresa anterior
    /// queda anulada (su QR marca "revocada") y la proxima impresion re-emite un
    /// QR nuevo con los medicamentos actualizados. El guard de HC abierta corre
    /// antes que esto, asi que aqui la HC siempre esta abierta (una HC cerrada es
    /// documento firmado y no se editan sus items). No hace SaveChanges: el metodo
    /// llamador persiste la revocacion junto con la edicion en el mismo scope.
    /// </summary>
    private async Task RevocarEmisionMedActivaAsync(Guid historiaId, int numeroOrden, Guid actor, CancellationToken ct)
    {
        var emisiones = await db.OrdenesMedicamentosPublicas
            .Where(o => o.HistoriaClinicaId == historiaId && o.TipoOrden == "MED"
                        && o.NumeroOrden == numeroOrden && o.RevocadaAt == null)
            .ToListAsync(ct);
        if (emisiones.Count == 0) { return; }

        var now = DateTimeOffset.UtcNow;
        foreach (var e in emisiones)
        {
            e.RevocadaAt = now;
            e.RevocadaPor = actor == Guid.Empty ? (Guid?)null : actor;
            e.RevocacionMotivo = "Revocada automaticamente al editar los medicamentos de la HC abierta.";
        }
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
