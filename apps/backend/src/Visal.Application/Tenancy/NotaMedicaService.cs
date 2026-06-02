using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class NotaMedicaService(IApplicationDbContext db, ITenantContext tenant) : INotaMedicaService
{
    public async Task<IReadOnlyList<NotaMedicaDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.NotasMedicas.AsNoTracking()
            .Where(n => n.HistoriaClinicaId == historiaId)
            .OrderByDescending(n => n.FechaNota).ThenByDescending(n => n.HoraNota)
            .Select(n => Map(n))
            .ToListAsync(ct);
    }

    public async Task<NotaConteoDto> ContarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        var defs = await db.NotasMedicas
            .CountAsync(n => n.HistoriaClinicaId == historiaId && n.Estado == NotaMedicaEstado.Definitivo, ct);
        var parc = await db.NotasMedicas
            .CountAsync(n => n.HistoriaClinicaId == historiaId && n.Estado == NotaMedicaEstado.Parcial, ct);
        return new NotaConteoDto(defs, parc);
    }

    public async Task<IReadOnlyList<NotaMedicaTarjetaDto>> ListarHistorialPacienteAsync(
        Guid pacienteId, CancellationToken ct = default)
    {
        return await db.NotasMedicas.AsNoTracking()
            .Where(n => n.PacienteId == pacienteId)
            .OrderByDescending(n => n.FechaNota).ThenByDescending(n => n.HoraNota)
            .Join(db.HistoriasClinicas.AsNoTracking(),
                  n => n.HistoriaClinicaId, h => h.Id,
                  (n, h) => new { n, h })
            .Join(db.FormDefinitions.AsNoTracking(),
                  x => x.h.FormDefinitionId, f => f.Id,
                  (x, f) => new { x.n, x.h, f })
            .Select(x => new NotaMedicaTarjetaDto(
                x.n.Id, x.n.CodigoUnico, x.n.FechaNota, x.n.HoraNota, x.n.SessionNo,
                x.n.Contenido.Length > 200 ? x.n.Contenido.Substring(0, 200) + "..." : x.n.Contenido,
                x.n.EspecialistaNombre, x.n.Estado.ToString(), x.n.Criticidad.ToString(),
                x.f.Codigo, x.f.Nombre))
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<NotaMedicaDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await db.NotasMedicas.AsNoTracking()
            .Where(n => n.Id == id)
            .Select(n => Map(n))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<NotaMedicaDto> GuardarAsync(
        GuardarNotaRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        NotaMedica entity;
        if (req.Id is Guid id)
        {
            entity = await db.NotasMedicas.FirstOrDefaultAsync(n => n.Id == id, ct)
                ?? throw new InvalidOperationException("Nota no encontrada.");
            // No se puede modificar una nota ya marcada como Definitivo.
            if (entity.Estado == NotaMedicaEstado.Definitivo)
            {
                throw new InvalidOperationException("La nota ya fue guardada como definitiva y no se puede modificar.");
            }
        }
        else
        {
            entity = new NotaMedica
            {
                TenantId = tid,
                HistoriaClinicaId = req.HistoriaClinicaId,
                PacienteId = req.PacienteId,
                AsignacionTurnoId = req.AsignacionTurnoId,
                SessionNo = req.SessionNo
            };
            db.NotasMedicas.Add(entity);
        }

        entity.FechaNota = req.FechaNota;
        entity.HoraNota = req.HoraNota;
        entity.Contenido = req.Contenido ?? "";
        entity.Estado = ParseEstado(req.Estado);
        entity.Criticidad = ParseCriticidad(req.Criticidad);
        entity.FirmaDataUrl = string.IsNullOrWhiteSpace(req.FirmaDataUrl) ? entity.FirmaDataUrl : req.FirmaDataUrl;
        // Solo seteamos el especialista en la creacion, no lo sobreescribimos
        // despues - el primer guardado marca quien hizo la nota.
        if (string.IsNullOrWhiteSpace(entity.EspecialistaNombre) && !string.IsNullOrWhiteSpace(req.EspecialistaNombre))
        {
            entity.EspecialistaNombre = req.EspecialistaNombre.Trim();
        }

        await db.SaveChangesAsync(ct);

        if (string.IsNullOrEmpty(entity.CodigoUnico))
        {
            entity.CodigoUnico = entity.Id.ToString()[..8];
            await db.SaveChangesAsync(ct);
        }
        return Map(entity);
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.NotasMedicas.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado == NotaMedicaEstado.Definitivo)
        {
            throw new InvalidOperationException("No se puede eliminar una nota ya definitiva.");
        }
        db.NotasMedicas.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ActualizarCriticidadAsync(
        Guid id, string criticidad, Guid actor, CancellationToken ct = default)
    {
        var e = await db.NotasMedicas.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (e is null) { return false; }
        e.Criticidad = ParseCriticidad(criticidad);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<NotaDocumentoDto>> ListarDocumentosAsync(
        Guid notaId, CancellationToken ct = default)
    {
        return await db.NotaMedicaDocumentos.AsNoTracking()
            .Where(d => d.NotaMedicaId == notaId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new NotaDocumentoDto(
                d.Id, d.NotaMedicaId, d.NombreOriginal, d.RutaArchivo,
                d.TipoMime, d.Tamano, d.Categoria, d.TipoTerapia, d.Mes,
                d.Anotaciones, d.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<NotaDocumentoDto> AdjuntarDocumentoAsync(
        AdjuntarDocumentoRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var entity = new NotaMedicaDocumento
        {
            TenantId = tid,
            NotaMedicaId = req.NotaMedicaId,
            NombreOriginal = req.NombreOriginal,
            RutaArchivo = req.RutaArchivo,
            TipoMime = req.TipoMime,
            Tamano = req.Tamano,
            Categoria = req.Categoria,
            TipoTerapia = req.TipoTerapia,
            Mes = req.Mes,
            Anotaciones = req.Anotaciones
        };
        db.NotaMedicaDocumentos.Add(entity);
        await db.SaveChangesAsync(ct);
        return new NotaDocumentoDto(
            entity.Id, entity.NotaMedicaId, entity.NombreOriginal, entity.RutaArchivo,
            entity.TipoMime, entity.Tamano, entity.Categoria, entity.TipoTerapia, entity.Mes,
            entity.Anotaciones, entity.CreatedAt);
    }

    public async Task<bool> EliminarDocumentoAsync(Guid documentoId, Guid actor, CancellationToken ct = default)
    {
        var e = await db.NotaMedicaDocumentos.FirstOrDefaultAsync(d => d.Id == documentoId, ct);
        if (e is null) { return false; }
        db.NotaMedicaDocumentos.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static NotaMedicaDto Map(NotaMedica n) => new(
        n.Id, n.HistoriaClinicaId, n.PacienteId, n.CodigoUnico,
        n.FechaNota, n.HoraNota, n.SessionNo, n.Contenido,
        n.EspecialistaNombre, n.Estado.ToString(), n.Criticidad.ToString(),
        n.FirmaDataUrl, n.CreatedAt);

    private static NotaMedicaEstado ParseEstado(string? s) =>
        Enum.TryParse<NotaMedicaEstado>(s, true, out var v) ? v : NotaMedicaEstado.Parcial;

    private static NotaMedicaCriticidad ParseCriticidad(string? s) =>
        Enum.TryParse<NotaMedicaCriticidad>(s, true, out var v) ? v : NotaMedicaCriticidad.Estable;
}
