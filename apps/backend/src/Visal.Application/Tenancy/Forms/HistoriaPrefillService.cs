using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Implementacion del servicio backend que aplica las rutas de prefill
/// "historiaMedica" al ValoresJson de la HC. Se llama desde los servicios de
/// items (OrdenMedicamentoService, RemisionService, IncapacidadService,
/// CertificacionService, OrdenServicioService) inmediatamente despues del
/// SaveChanges del item — mismo scope, misma transaccion EF Core.
/// </summary>
public sealed class HistoriaPrefillService(IApplicationDbContext db) : IHistoriaPrefillService
{
    public async Task ActualizarValoresAsync(Guid historiaId, CancellationToken ct = default)
    {
        var hc = await db.HistoriasClinicas
            .Where(h => h.Id == historiaId)
            .FirstOrDefaultAsync(ct);
        if (hc is null) { return; }

        var formDef = await db.FormDefinitions
            .Where(f => f.Id == hc.FormDefinitionId)
            .FirstOrDefaultAsync(ct);
        if (formDef is null) { return; }

        var routes = PrefillRouteSet.FromJson(formDef.PrefillRoutesJson);
        if (routes.Routes.Count == 0) { return; }
        // Si no hay ruta historiaMedica configurada, no hay nada que hacer.
        if (!routes.Routes.Any(r => string.Equals(r.SourceModule, "historiaMedica", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var schema = FormSchema.FromJson(formDef.SchemaJson);

        // Cargar las 5 listas de items que el helper consume.
        var meds = await db.HistoriaClinicaMedicamentos
            .Where(m => m.HistoriaClinicaId == historiaId)
            .OrderBy(m => m.Orden)
            .Select(m => new OrdenMedicamentoItemDto(
                m.Id, m.HistoriaClinicaId, m.MedicamentoId, m.CodigoMedicamento, m.NombreMedicamento,
                m.Cantidad, m.Frecuencia, m.Dias, m.Posologia, m.Observacion, m.Orden))
            .ToListAsync(ct);

        var rem = await db.HistoriaClinicaRemisiones
            .Where(r => r.HistoriaClinicaId == historiaId)
            .OrderBy(r => r.Orden)
            .Select(r => new RemisionItemDto(
                r.Id, r.HistoriaClinicaId, r.EspecialidadCodigo,
                r.EspecialidadNombre, r.Cantidad, r.Motivo, r.Orden))
            .ToListAsync(ct);

        var inc = await db.HistoriaClinicaIncapacidades
            .Where(i => i.HistoriaClinicaId == historiaId)
            .OrderBy(i => i.Orden)
            .Select(i => new IncapacidadItemDto(
                i.Id, i.HistoriaClinicaId, i.Motivo, i.FechaDesde, i.FechaHasta,
                i.Dias, i.Tipo, i.Orden))
            .ToListAsync(ct);

        var cert = await db.HistoriaClinicaCertificaciones
            .Where(c => c.HistoriaClinicaId == historiaId)
            .OrderBy(c => c.Orden)
            .Select(c => new CertificacionItemDto(
                c.Id, c.HistoriaClinicaId, c.Titulo, c.Contenido, c.Orden))
            .ToListAsync(ct);

        var ord = await db.HistoriaClinicaOrdenesServicio
            .Where(o => o.HistoriaClinicaId == historiaId)
            .OrderBy(o => o.Orden)
            .Select(o => new OrdenServicioItemDto(
                o.Id, o.HistoriaClinicaId, o.ServicioContratoId, o.CodigoServicio,
                o.Descripcion, o.Cantidad, o.Observaciones, o.Orden))
            .ToListAsync(ct);

        var ins = await db.HistoriaClinicaInsumos
            .Where(o => o.HistoriaClinicaId == historiaId)
            .OrderBy(o => o.Orden)
            .Select(o => new InsumoItemDto(
                o.Id, o.HistoriaClinicaId, o.Codigo, o.Descripcion,
                o.Cantidad, o.Observaciones, o.MipresUrl, o.Orden))
            .ToListAsync(ct);

        // Ordenes externas por tipo (RX imagenologia, laboratorios, insumos externos).
        // Los "servicios externos" ya se unificaron en historia_clinica_ordenes_servicio
        // (arriba, `ord`), asi que no necesitan traerse aparte.
        var externas = await db.HistoriaClinicaOrdenesExternas
            .Where(o => o.HistoriaClinicaId == historiaId)
            .OrderBy(o => o.Orden)
            .Select(o => new { o.Id, o.Orden, o.Tipo, o.Codigo, o.Descripcion, o.Cantidad, o.Observaciones })
            .ToListAsync(ct);
        var rxExt  = externas.Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.RxImagenologia)
                             .Select(x => new OrdenExternaItemDto(x.Id, x.Orden, x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones))
                             .ToList();
        var labExt = externas.Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.Laboratorio)
                             .Select(x => new OrdenExternaItemDto(x.Id, x.Orden, x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones))
                             .ToList();
        var insExt = externas.Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.Insumo)
                             .Select(x => new OrdenExternaItemDto(x.Id, x.Orden, x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones))
                             .ToList();

        var fuentes = new HistoriaMedicaPrefillHelper.HmFuentes(meds, rem, inc, cert, ord, ins, rxExt, labExt, insExt);

        // Deserializar el ValoresJson actual, aplicar el helper, re-serializar.
        var valores = DeserializarValores(hc.ValoresJson);
        HistoriaMedicaPrefillHelper.Aplicar(valores, fuentes, routes, schema);
        hc.ValoresJson = JsonSerializer.Serialize(valores);

        // Marcar la entidad como modificada y dejar pendiente para el siguiente
        // SaveChanges del caller (que ya esta a punto de guardar el item).
        // No llamamos SaveChanges aqui — eso lo hace el caller dentro de su
        // misma transaccion, asi todo es atomico.
        await db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, string?> DeserializarValores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            return dict is null
                ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string?>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
