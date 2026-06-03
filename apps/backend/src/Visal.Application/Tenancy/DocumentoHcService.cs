using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class DocumentoHcService(IApplicationDbContext db, ITenantContext tenant) : IDocumentoHcService
{
    private static string NormalizarTipo(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
        { throw new InvalidOperationException("Tipo de documento es obligatorio (EVOLUCION o CONSENTIMIENTO)."); }
        return tipo.Trim().ToUpperInvariant();
    }

    public async Task<IReadOnlyList<DocumentoHcFormatoDto>> ListarFormatosDisponiblesAsync(
        Guid historiaId, string tipo, CancellationToken ct = default)
    {
        var t = NormalizarTipo(tipo);

        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == historiaId)
            .Select(h => new { h.FormDefinitionId })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Historia clinica no encontrada.");

        // Join relaciones_formulario -> FormDefinitions destino para obtener metadatos
        // del formato sugerido. Materializamos antes del OrderBy para evitar el problema
        // clasico de OrderBy sobre propiedades de un record proyectado desde joins.
        var rows = await db.RelacionesFormulario.AsNoTracking()
            .Where(r => r.FormularioOrigenId == hc.FormDefinitionId
                        && r.Activo
                        && r.TipoRelacion != null
                        && r.TipoRelacion == t)
            .Join(db.FormDefinitions.AsNoTracking(), r => r.FormularioDestinoId, f => f.Id, (r, f) => new
            {
                f.Id, f.Codigo, f.Nombre, f.Version, f.Tipo, f.Activo, r.Observacion
            })
            .ToListAsync(ct);
        return rows
            .Where(r => r.Activo)
            .OrderBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(r => new DocumentoHcFormatoDto(r.Id, r.Codigo, r.Nombre, r.Version, r.Tipo, r.Activo, r.Observacion))
            .ToList();
    }

    public async Task<IReadOnlyList<DocumentoHcItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, string tipo, CancellationToken ct = default)
    {
        var t = NormalizarTipo(tipo);
        var rows = await db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(d => d.HistoriaClinicaId == historiaId && d.Tipo == t)
            .Join(db.FormDefinitions.AsNoTracking(), d => d.FormDefinitionId, f => f.Id, (d, f) => new
            {
                d.Id, d.HistoriaClinicaId, d.FormDefinitionId, d.Tipo,
                FormatoCodigo = f.Codigo, FormatoNombre = f.Nombre,
                d.Estado, d.FechaApertura, d.FechaCierre, d.EspecialistaNombre
            })
            .ToListAsync(ct);
        return rows
            .OrderByDescending(r => r.FechaApertura)
            .Select(r => new DocumentoHcItemDto(
                r.Id, r.HistoriaClinicaId, r.FormDefinitionId, r.Tipo,
                r.FormatoCodigo, r.FormatoNombre,
                r.Estado.ToString(), r.FechaApertura, r.FechaCierre,
                r.EspecialistaNombre))
            .ToList();
    }

    public async Task<DocumentoHcDetailDto> IniciarAsync(IniciarDocumentoHcRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var t = NormalizarTipo(req.Tipo);

        var hc = await db.HistoriasClinicas.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == req.HistoriaClinicaId, ct)
            ?? throw new InvalidOperationException("Historia clinica no encontrada.");
        if (hc.Estado == HistoriaClinicaEstado.Inactiva)
        { throw new InvalidOperationException("La historia clinica esta inactiva; no se pueden iniciar documentos."); }

        var formato = await db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == req.FormDefinitionId, ct)
            ?? throw new InvalidOperationException("Formato no encontrado.");

        // Verificar que el formato destino esta relacionado con el origen (la HC) y
        // que la relacion sea del tipo correspondiente. Evita que la UI sortee la
        // configuracion enviando un FormDefId arbitrario.
        var relOk = await db.RelacionesFormulario.AsNoTracking().AnyAsync(
            r => r.FormularioOrigenId == hc.FormDefinitionId
                 && r.FormularioDestinoId == req.FormDefinitionId
                 && r.Activo
                 && r.TipoRelacion == t, ct);
        if (!relOk)
        { throw new InvalidOperationException("El formato no esta configurado como " + t + " para esta historia."); }

        var entity = new HistoriaClinicaDocumento
        {
            TenantId = tid,
            HistoriaClinicaId = req.HistoriaClinicaId,
            FormDefinitionId = req.FormDefinitionId,
            Tipo = t,
            ValoresJson = string.IsNullOrWhiteSpace(req.ValoresJson) ? "{}" : req.ValoresJson,
            Estado = HistoriaClinicaEstado.Abierta,
            FechaApertura = DateTimeOffset.UtcNow,
            EspecialistaNombre = req.EspecialistaNombre
        };
        db.HistoriaClinicaDocumentos.Add(entity);
        await db.SaveChangesAsync(ct);

        return new DocumentoHcDetailDto(
            entity.Id, entity.HistoriaClinicaId, entity.FormDefinitionId, entity.Tipo,
            formato.Codigo, formato.Nombre, formato.Version,
            formato.SchemaJson, formato.PrefillRoutesJson, entity.ValoresJson,
            entity.Estado.ToString(), entity.FechaApertura, entity.FechaCierre,
            entity.EspecialistaNombre);
    }

    public async Task<DocumentoHcDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(d => d.Id == id)
            .Join(db.FormDefinitions.AsNoTracking(), d => d.FormDefinitionId, f => f.Id, (d, f) => new
            {
                d.Id, d.HistoriaClinicaId, d.FormDefinitionId, d.Tipo,
                FormatoCodigo = f.Codigo, FormatoNombre = f.Nombre, FormatoVersion = f.Version,
                f.SchemaJson, f.PrefillRoutesJson, d.ValoresJson,
                d.Estado, d.FechaApertura, d.FechaCierre, d.EspecialistaNombre
            })
            .FirstOrDefaultAsync(ct);
        return r is null ? null : new DocumentoHcDetailDto(
            r.Id, r.HistoriaClinicaId, r.FormDefinitionId, r.Tipo,
            r.FormatoCodigo, r.FormatoNombre, r.FormatoVersion,
            r.SchemaJson, r.PrefillRoutesJson, r.ValoresJson,
            r.Estado.ToString(), r.FechaApertura, r.FechaCierre,
            r.EspecialistaNombre);
    }

    public async Task<bool> GuardarValoresAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var d = await db.HistoriaClinicaDocumentos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) { return false; }
        d.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? "{}" : valoresJson;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CerrarAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var d = await db.HistoriaClinicaDocumentos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) { return false; }
        d.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? d.ValoresJson : valoresJson;
        d.Estado = HistoriaClinicaEstado.Cerrada;
        d.FechaCierre = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var d = await db.HistoriaClinicaDocumentos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) { return false; }
        db.HistoriaClinicaDocumentos.Remove(d);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ContarPorHistoriaAsync(Guid historiaId, string tipo, CancellationToken ct = default)
    {
        var t = NormalizarTipo(tipo);
        return await db.HistoriaClinicaDocumentos.CountAsync(d => d.HistoriaClinicaId == historiaId && d.Tipo == t, ct);
    }
}
