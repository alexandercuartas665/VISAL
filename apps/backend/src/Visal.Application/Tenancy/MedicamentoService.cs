using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class MedicamentoService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit) : IMedicamentoService
{
    public async Task<(IReadOnlyList<MedicamentoDto> rows, int total)> SearchAsync(
        string? termino, int skip, int take, CancellationToken ct = default)
    {
        var q = db.Medicamentos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(termino))
        {
            // Busqueda case-insensitive portable: comparar en lowercase ambos lados.
            // Postgres traduce ToLower() a lower() sin issues.
            var t = termino.Trim().ToLowerInvariant();
            q = q.Where(m =>
                (m.Producto != null && m.Producto.ToLower().Contains(t)) ||
                (m.PrincipioActivo != null && m.PrincipioActivo.ToLower().Contains(t)) ||
                (m.Atc != null && m.Atc.ToLower().Contains(t)) ||
                (m.Ium != null && m.Ium.ToLower().Contains(t)) ||
                (m.RegistroSanitario != null && m.RegistroSanitario.ToLower().Contains(t)) ||
                (m.DescripcionComercial != null && m.DescripcionComercial.ToLower().Contains(t)));
        }

        var total = await q.CountAsync(ct);
        if (take <= 0) { take = 50; }
        if (take > 500) { take = 500; }

        var rows = await q
            .OrderBy(m => m.Producto)
            .ThenBy(m => m.RegistroSanitario)
            .Skip(skip).Take(take)
            .Select(m => new MedicamentoDto(
                m.Id, m.Producto, m.RegistroSanitario, m.PrincipioActivo,
                m.Concentracion, m.FormaFarmaceutica, m.DescripcionComercial,
                m.EstadoRegistro, m.EstadoCum, m.Ium))
            .ToListAsync(ct);
        return (rows, total);
    }

    public async Task<MedicamentoDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await db.Medicamentos.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new MedicamentoDetailDto(
                m.Id, m.Expediente, m.Producto, m.Titular, m.RegistroSanitario,
                m.FechaExpedicion, m.FechaVencimiento, m.EstadoRegistro,
                m.ExpedienteCum, m.ConsecutivoCum, m.CantidadCum, m.DescripcionComercial,
                m.EstadoCum, m.FechaActivo, m.FechaInactivo, m.MuestraMedica, m.Unidad,
                m.Atc, m.DescripcionAtc, m.ViaAdministracion, m.Concentracion,
                m.PrincipioActivo, m.UnidadMedida, m.Cantidad, m.UnidadReferencia,
                m.FormaFarmaceutica, m.NombreRol, m.TipoRol, m.Modalidad, m.Ium))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<MedicamentoDetailDto?> SaveAsync(SaveMedicamentoRequest r, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        Medicamento entity;
        if (r.Id is Guid id)
        {
            entity = await db.Medicamentos.FirstOrDefaultAsync(m => m.Id == id, ct)
                ?? throw new InvalidOperationException("Medicamento no encontrado.");
        }
        else
        {
            entity = new Medicamento { TenantId = tid };
            db.Medicamentos.Add(entity);
        }

        entity.Expediente = Norm(r.Expediente);
        entity.Producto = Norm(r.Producto);
        entity.Titular = Norm(r.Titular);
        entity.RegistroSanitario = Norm(r.RegistroSanitario);
        entity.FechaExpedicion = r.FechaExpedicion;
        entity.FechaVencimiento = r.FechaVencimiento;
        entity.EstadoRegistro = Norm(r.EstadoRegistro);
        entity.ExpedienteCum = Norm(r.ExpedienteCum);
        entity.ConsecutivoCum = Norm(r.ConsecutivoCum);
        entity.CantidadCum = Norm(r.CantidadCum);
        entity.DescripcionComercial = Norm(r.DescripcionComercial);
        entity.EstadoCum = Norm(r.EstadoCum);
        entity.FechaActivo = r.FechaActivo;
        entity.FechaInactivo = r.FechaInactivo;
        entity.MuestraMedica = Norm(r.MuestraMedica);
        entity.Unidad = Norm(r.Unidad);
        entity.Atc = Norm(r.Atc);
        entity.DescripcionAtc = Norm(r.DescripcionAtc);
        entity.ViaAdministracion = Norm(r.ViaAdministracion);
        entity.Concentracion = Norm(r.Concentracion);
        entity.PrincipioActivo = Norm(r.PrincipioActivo);
        entity.UnidadMedida = Norm(r.UnidadMedida);
        entity.Cantidad = Norm(r.Cantidad);
        entity.UnidadReferencia = Norm(r.UnidadReferencia);
        entity.FormaFarmaceutica = Norm(r.FormaFarmaceutica);
        entity.NombreRol = Norm(r.NombreRol);
        entity.TipoRol = Norm(r.TipoRol);
        entity.Modalidad = Norm(r.Modalidad);
        entity.Ium = Norm(r.Ium);

        audit.Write(actor,
            r.Id is null ? "medicamento.create" : "medicamento.update",
            nameof(Medicamento), entity.Id,
            previousValue: null, newValue: new { entity.Producto, entity.RegistroSanitario }, tenantId: tid);

        await db.SaveChangesAsync(ct);
        return await GetAsync(entity.Id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.Medicamentos.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (e is null) { return false; }
        db.Medicamentos.Remove(e);
        audit.Write(actor, "medicamento.delete", nameof(Medicamento), e.Id,
            previousValue: new { e.Producto, e.RegistroSanitario }, newValue: null, tenantId: e.TenantId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ImportAsync(
        IReadOnlyList<MedicamentoImportRow> rows,
        Guid actor,
        IProgress<MedicamentoImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (rows.Count == 0) { return 0; }

        // Fase 1: validar y mapear a entidades en memoria. Reportamos cada ~1000 filas.
        var insert = new List<Medicamento>(rows.Count);
        const int chunkValidacion = 1000;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            // Saltar filas vacias
            if (string.IsNullOrWhiteSpace(r.Producto)
                && string.IsNullOrWhiteSpace(r.RegistroSanitario)
                && string.IsNullOrWhiteSpace(r.PrincipioActivo))
            { continue; }

            insert.Add(new Medicamento
            {
                TenantId = tid,
                Expediente = Norm(r.Expediente),
                Producto = Norm(r.Producto),
                Titular = Norm(r.Titular),
                RegistroSanitario = Norm(r.RegistroSanitario),
                FechaExpedicion = ParseDate(r.FechaExpedicion),
                FechaVencimiento = ParseDate(r.FechaVencimiento),
                EstadoRegistro = Norm(r.EstadoRegistro),
                ExpedienteCum = Norm(r.ExpedienteCum),
                ConsecutivoCum = Norm(r.ConsecutivoCum),
                CantidadCum = Norm(r.CantidadCum),
                DescripcionComercial = Norm(r.DescripcionComercial),
                EstadoCum = Norm(r.EstadoCum),
                FechaActivo = ParseDate(r.FechaActivo),
                FechaInactivo = ParseDate(r.FechaInactivo),
                MuestraMedica = Norm(r.MuestraMedica),
                Unidad = Norm(r.Unidad),
                Atc = Norm(r.Atc),
                DescripcionAtc = Norm(r.DescripcionAtc),
                ViaAdministracion = Norm(r.ViaAdministracion),
                Concentracion = Norm(r.Concentracion),
                PrincipioActivo = Norm(r.PrincipioActivo),
                UnidadMedida = Norm(r.UnidadMedida),
                Cantidad = Norm(r.Cantidad),
                UnidadReferencia = Norm(r.UnidadReferencia),
                FormaFarmaceutica = Norm(r.FormaFarmaceutica),
                NombreRol = Norm(r.NombreRol),
                TipoRol = Norm(r.TipoRol),
                Modalidad = Norm(r.Modalidad),
                Ium = Norm(r.Ium)
            });

            if (progress is not null && (i + 1) % chunkValidacion == 0)
            {
                progress.Report(new MedicamentoImportProgress("Validando", i + 1, rows.Count));
            }
            ct.ThrowIfCancellationRequested();
        }
        progress?.Report(new MedicamentoImportProgress("Validando", rows.Count, rows.Count));

        // Fase 2: insertar en lotes para mantener viable un archivo de 100k+ filas.
        // EF + Postgres puede hacer batches grandes pero el cambio set crece, asi
        // que cortamos cada N para no quemar memoria ni el round-trip de save.
        const int chunkInsercion = 500;
        var insertados = 0;
        for (int offset = 0; offset < insert.Count; offset += chunkInsercion)
        {
            var batch = insert.Skip(offset).Take(chunkInsercion).ToList();
            db.Medicamentos.AddRange(batch);
            await db.SaveChangesAsync(ct);
            insertados += batch.Count;
            progress?.Report(new MedicamentoImportProgress("Insertando", insertados, insert.Count));
            ct.ThrowIfCancellationRequested();
        }

        audit.Write(actor, "medicamento.import", nameof(Medicamento), Guid.Empty,
            previousValue: null, newValue: new { count = insertados }, tenantId: tid);

        progress?.Report(new MedicamentoImportProgress("Listo", insertados, insertados));
        return insertados;
    }

    public async Task<int> ClearAllAsync(Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var n = await db.Medicamentos.ExecuteDeleteAsync(ct);
        audit.Write(actor, "medicamento.clear-all", nameof(Medicamento), Guid.Empty,
            previousValue: new { count = n }, newValue: null, tenantId: tid);
        return n;
    }

    private static string? Norm(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Parsea fechas del CUM tolerando los formatos comunes:
    /// MM/dd/yyyy (US), dd/MM/yyyy (es-CO), yyyy-MM-dd (ISO).
    /// </summary>
    private static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var t = s.Trim();
        string[] formats = { "MM/dd/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "d/M/yyyy" };
        if (DateOnly.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d;
        }
        if (DateOnly.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
        {
            return d;
        }
        return null;
    }
}
