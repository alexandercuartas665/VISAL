using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class SuministroMedicamentoService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IHistoriaPrefillService prefill) : ISuministroMedicamentoService
{
    public async Task<IReadOnlyList<SuministroMedicamentoItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaSuministroMedicamentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == historiaId)
            .OrderBy(x => x.FechaHora)
            .ThenBy(x => x.Orden)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new SuministroMedicamentoItemDto(
                x.Id, x.HistoriaClinicaId, x.FechaHora, x.Presentacion,
                x.Dosis, x.Cantidad, x.Via,
                x.UsuarioCreacionId, x.UsuarioCreacionNombre, x.Orden))
            .ToListAsync(ct);
    }

    public async Task<SuministroMedicamentoItemDto> AgregarAsync(
        Guid historiaId, AgregarSuministroMedicamentoRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Presentacion))
        {
            throw new InvalidOperationException("La presentacion (nombre) del medicamento es obligatoria.");
        }
        await db.EnsureAbiertaAsync(historiaId, ct);

        var siguiente = 1 + await db.HistoriaClinicaSuministroMedicamentos
            .Where(x => x.HistoriaClinicaId == historiaId)
            .Select(x => (int?)x.Orden).MaxAsync(ct) ?? 1;

        // Snapshot del nombre del usuario para que la impresion siga viendose
        // correcta aun si el usuario es renombrado o desactivado. Preferimos
        // DisplayName; si no hay, componemos con los campos existentes.
        var actorId = tenant.UserId ?? actor;
        var usr = actorId != Guid.Empty
            ? await db.PlatformUsers.AsNoTracking()
                .Where(u => u.Id == actorId)
                .Select(u => new { u.DisplayName, u.PrimerNombre, u.SegundoNombre, u.Username })
                .FirstOrDefaultAsync(ct)
            : null;
        var nombre = ComponerNombre(usr?.DisplayName, usr?.PrimerNombre, usr?.SegundoNombre, usr?.Username);

        var entity = new HistoriaClinicaSuministroMedicamento
        {
            TenantId = tid,
            HistoriaClinicaId = historiaId,
            FechaHora = req.FechaHora,
            Presentacion = req.Presentacion.Trim(),
            Dosis = Trim(req.Dosis),
            Cantidad = Trim(req.Cantidad),
            Via = Trim(req.Via),
            UsuarioCreacionId = actorId == Guid.Empty ? null : actorId,
            UsuarioCreacionNombre = nombre,
            Orden = siguiente
        };
        db.HistoriaClinicaSuministroMedicamentos.Add(entity);
        await db.SaveChangesAsync(ct);
        // Refrescar historiaMedica.suministros_medicamentos en el ValoresJson
        // de la HC para que los campos/tablas configurados en el formulario se
        // actualicen en vivo (mismo patron que Insumos/Medicamentos).
        await prefill.ActualizarValoresAsync(historiaId, ct);

        return new SuministroMedicamentoItemDto(
            entity.Id, entity.HistoriaClinicaId, entity.FechaHora, entity.Presentacion,
            entity.Dosis, entity.Cantidad, entity.Via,
            entity.UsuarioCreacionId, entity.UsuarioCreacionNombre, entity.Orden);
    }

    public async Task<bool> ActualizarAsync(
        Guid itemId, ActualizarSuministroMedicamentoRequest req, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaSuministroMedicamentos.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        if (string.IsNullOrWhiteSpace(req.Presentacion))
        {
            throw new InvalidOperationException("La presentacion (nombre) del medicamento es obligatoria.");
        }
        await db.EnsureAbiertaAsync(entity.HistoriaClinicaId, ct);
        // No tocamos UsuarioCreacion* — es snapshot del original.
        entity.FechaHora = req.FechaHora;
        entity.Presentacion = req.Presentacion.Trim();
        entity.Dosis = Trim(req.Dosis);
        entity.Cantidad = Trim(req.Cantidad);
        entity.Via = Trim(req.Via);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(entity.HistoriaClinicaId, ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.HistoriaClinicaSuministroMedicamentos.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (entity is null) { return false; }
        var hcId = entity.HistoriaClinicaId;
        await db.EnsureAbiertaAsync(hcId, ct);
        db.HistoriaClinicaSuministroMedicamentos.Remove(entity);
        await db.SaveChangesAsync(ct);
        await prefill.ActualizarValoresAsync(hcId, ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaSuministroMedicamentos
            .CountAsync(x => x.HistoriaClinicaId == historiaId, ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? ComponerNombre(string? display, string? p1, string? p2, string? user)
    {
        if (!string.IsNullOrWhiteSpace(display)) { return display.Trim(); }
        var partes = new[] { p1, p2 }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim());
        var compuesto = string.Join(' ', partes);
        if (!string.IsNullOrWhiteSpace(compuesto)) { return compuesto; }
        return Trim(user);
    }
}
