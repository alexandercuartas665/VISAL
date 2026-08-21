using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class EscalaService(IApplicationDbContext db, ITenantContext tenant) : IEscalaService
{
    public async Task<IReadOnlyList<EscalaFormatoDto>> ListarFormatosDisponiblesAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        // Resolvemos el FormDefinitionId del formato de la HC padre; sin el no hay
        // forma de saber que escalas se sugieren para esta HC.
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == historiaId)
            .Select(h => new { h.FormDefinitionId })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Historia clinica no encontrada.");

        // Join relaciones_formulario -> FormDefinitions destino. Materializamos
        // antes del OrderBy porque EF Core 9 no traduce OrderBy sobre propiedades
        // de un record DTO proyectado desde joins.
        var rows = await db.RelacionesFormulario.AsNoTracking()
            .Where(r => r.FormularioOrigenId == hc.FormDefinitionId
                        && r.Activo
                        && r.TipoRelacion != null
                        && r.TipoRelacion == "ESCALA")
            .Join(db.FormDefinitions.AsNoTracking(), r => r.FormularioDestinoId, f => f.Id, (r, f) => new
            {
                f.Id, f.Codigo, f.Nombre, f.Version, f.Tipo, f.Activo
            })
            .ToListAsync(ct);
        return rows
            .Where(r => r.Activo)
            .OrderBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(r => new EscalaFormatoDto(r.Id, r.Codigo, r.Nombre, r.Version, r.Tipo, r.Activo))
            .ToList();
    }

    public async Task<IReadOnlyList<EscalaItemDto>> ListarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        // OrderBy se hace tras materializar porque EF Core 9 no traduce OrderBy
        // sobre propiedades de un record DTO proyectado desde joins.
        var rows = await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(e => e.HistoriaClinicaId == historiaId)
            .Join(db.FormDefinitions.AsNoTracking(), e => e.FormDefinitionId, f => f.Id, (e, f) => new
            {
                e.Id, e.HistoriaClinicaId, e.FormDefinitionId,
                FormatoCodigo = f.Codigo, FormatoNombre = f.Nombre,
                e.Estado, e.FechaApertura, e.FechaCierre, e.EspecialistaNombre
            })
            .ToListAsync(ct);
        return rows
            .OrderByDescending(r => r.FechaApertura)
            .Select(r => new EscalaItemDto(
                r.Id, r.HistoriaClinicaId, r.FormDefinitionId,
                r.FormatoCodigo, r.FormatoNombre,
                r.Estado.ToString(), r.FechaApertura, r.FechaCierre,
                r.EspecialistaNombre))
            .ToList();
    }

    public async Task<EscalaDetailDto> IniciarAsync(IniciarEscalaRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        await db.EnsureAbiertaAsync(req.HistoriaClinicaId, ct);

        var hc = await db.HistoriasClinicas.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == req.HistoriaClinicaId, ct)
            ?? throw new InvalidOperationException("Historia clinica no encontrada.");
        if (hc.Estado == HistoriaClinicaEstado.Inactiva)
        { throw new InvalidOperationException("La historia clinica esta inactiva; no se pueden iniciar escalas."); }

        var formato = await db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == req.FormDefinitionId, ct)
            ?? throw new InvalidOperationException("Formato de escala no encontrado.");

        // Verificar que el formato destino esta configurado como ESCALA para el
        // formato de esta HC. Evita que la UI sortee la configuracion enviando
        // un FormDefId arbitrario del catalogo.
        var relOk = await db.RelacionesFormulario.AsNoTracking().AnyAsync(
            r => r.FormularioOrigenId == hc.FormDefinitionId
                 && r.FormularioDestinoId == req.FormDefinitionId
                 && r.Activo
                 && r.TipoRelacion == "ESCALA", ct);
        if (!relOk)
        { throw new InvalidOperationException("El formato no esta configurado como ESCALA para esta historia."); }

        var entity = new HistoriaClinicaEscala
        {
            TenantId = tid,
            HistoriaClinicaId = req.HistoriaClinicaId,
            FormDefinitionId = req.FormDefinitionId,
            ValoresJson = string.IsNullOrWhiteSpace(req.ValoresJson) ? "{}" : req.ValoresJson,
            Estado = HistoriaClinicaEstado.Abierta,
            FechaApertura = DateTimeOffset.UtcNow,
            EspecialistaNombre = req.EspecialistaNombre
        };
        db.HistoriaClinicaEscalas.Add(entity);
        await db.SaveChangesAsync(ct);

        return new EscalaDetailDto(
            entity.Id, entity.HistoriaClinicaId, entity.FormDefinitionId,
            formato.Codigo, formato.Nombre, formato.Version,
            formato.SchemaJson, formato.PrefillRoutesJson, entity.ValoresJson,
            entity.Estado.ToString(), entity.FechaApertura, entity.FechaCierre,
            entity.EspecialistaNombre);
    }

    public async Task<EscalaDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        // Misma tactica: proyectar a anonimo, materializar, mapear al record.
        var r = await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(e => e.Id == id)
            .Join(db.FormDefinitions.AsNoTracking(), e => e.FormDefinitionId, f => f.Id, (e, f) => new
            {
                e.Id, e.HistoriaClinicaId, e.FormDefinitionId,
                FormatoCodigo = f.Codigo, FormatoNombre = f.Nombre, FormatoVersion = f.Version,
                f.SchemaJson, f.PrefillRoutesJson, e.ValoresJson,
                e.Estado, e.FechaApertura, e.FechaCierre, e.EspecialistaNombre
            })
            .FirstOrDefaultAsync(ct);
        return r is null ? null : new EscalaDetailDto(
            r.Id, r.HistoriaClinicaId, r.FormDefinitionId,
            r.FormatoCodigo, r.FormatoNombre, r.FormatoVersion,
            r.SchemaJson, r.PrefillRoutesJson, r.ValoresJson,
            r.Estado.ToString(), r.FechaApertura, r.FechaCierre,
            r.EspecialistaNombre);
    }

    public async Task<bool> GuardarValoresAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriaClinicaEscalas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        await db.EnsureAbiertaAsync(e.HistoriaClinicaId, ct);
        e.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? "{}" : valoresJson;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CerrarAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriaClinicaEscalas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        await db.EnsureAbiertaAsync(e.HistoriaClinicaId, ct);
        e.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? e.ValoresJson : valoresJson;
        e.Estado = HistoriaClinicaEstado.Cerrada;
        e.FechaCierre = DateTimeOffset.UtcNow;

        // Al cerrar, vuelca un resumen automatico de la escala al campo de la HC
        // que el admin haya mapeado en Rutas de prefill (origen "escalas.resumen").
        // Modifica la entidad HC (tracked); el SaveChanges de abajo persiste ambas.
        await VolcarResumenAAnalisisAsync(e, ct);

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Si el formato de la HC padre tiene una ruta de prefill con origen "escalas"
    /// (campo "resumen") mapeada a un campo destino, construye el resumen automatico
    /// de esta escala (nombre + fecha + campos de la seccion RESULTADO) y lo ACUMULA
    /// (append) en ese campo de <c>HistoriaClinica.ValoresJson</c>. Sin ruta
    /// configurada, no hace nada (comportamiento historico intacto).
    /// </summary>
    private async Task VolcarResumenAAnalisisAsync(HistoriaClinicaEscala e, CancellationToken ct)
    {
        var hc = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == e.HistoriaClinicaId, ct);
        if (hc is null) { return; }

        // Campo destino: target del mapeo con origen "escalas" / source "resumen".
        var hcForm = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == hc.FormDefinitionId)
            .Select(f => new { f.PrefillRoutesJson })
            .FirstOrDefaultAsync(ct);
        if (hcForm is null) { return; }

        var target = PrefillRouteSet.FromJson(hcForm.PrefillRoutesJson).Routes
            .Where(r => string.Equals(r.SourceModule, "escalas", StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Mappings)
            .Where(m => string.Equals(m.Source, "resumen", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(m.Target))
            .Select(m => m.Target)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(target)) { return; }

        var escForm = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == e.FormDefinitionId)
            .Select(f => new { f.Nombre, f.SchemaJson })
            .FirstOrDefaultAsync(ct);
        if (escForm is null) { return; }

        var bloque = EscalaResumenBuilder.Construir(
            escForm.SchemaJson, e.ValoresJson, escForm.Nombre, e.FechaCierre ?? DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(bloque)) { return; }

        var valores = ParseValores(hc.ValoresJson);
        valores.TryGetValue(target, out var actual);
        valores[target] = string.IsNullOrWhiteSpace(actual)
            ? bloque
            : actual!.TrimEnd() + "\n\n" + bloque;
        hc.ValoresJson = JsonSerializer.Serialize(valores);
    }

    private static Dictionary<string, string?> ParseValores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriaClinicaEscalas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        await db.EnsureAbiertaAsync(e.HistoriaClinicaId, ct);
        db.HistoriaClinicaEscalas.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default)
    {
        return await db.HistoriaClinicaEscalas.CountAsync(e => e.HistoriaClinicaId == historiaId, ct);
    }
}
