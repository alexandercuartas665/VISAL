using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class RelacionFormularioService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit) : IRelacionFormularioService
{
    public async Task<IReadOnlyList<RelacionFormularioDto>> ListarAsync(CancellationToken ct = default)
    {
        // OrderBy se hace despues de materializar porque EF Core 9 no traduce
        // OrderBy sobre propiedades de un record proyectado desde joins.
        var rows = await db.RelacionesFormulario.AsNoTracking()
            .Join(db.FormDefinitions.AsNoTracking(), r => r.FormularioOrigenId, o => o.Id, (r, o) => new { r, o })
            .Join(db.FormDefinitions.AsNoTracking(), x => x.r.FormularioDestinoId, d => d.Id, (x, d) => new
            {
                x.r.Id,
                OrigenId = x.o.Id, OrigenCodigo = x.o.Codigo, OrigenNombre = x.o.Nombre, OrigenTipo = x.o.Tipo,
                DestinoId = d.Id, DestinoCodigo = d.Codigo, DestinoNombre = d.Nombre, DestinoTipo = d.Tipo,
                x.r.TipoRelacion, x.r.Activo, x.r.Observacion
            })
            .ToListAsync(ct);
        return rows
            .OrderBy(r => r.OrigenNombre, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.DestinoNombre, StringComparer.OrdinalIgnoreCase)
            .Select(r => new RelacionFormularioDto(
                r.Id,
                r.OrigenId, r.OrigenCodigo, r.OrigenNombre, r.OrigenTipo,
                r.DestinoId, r.DestinoCodigo, r.DestinoNombre, r.DestinoTipo,
                r.TipoRelacion, r.Activo, r.Observacion))
            .ToList();
    }

    public async Task<IReadOnlyList<OpcionFormularioDto>> ListarOpcionesAsync(CancellationToken ct = default)
    {
        return await db.FormDefinitions.AsNoTracking()
            .OrderBy(f => f.Tipo).ThenBy(f => f.Nombre)
            .Select(f => new OpcionFormularioDto(f.Id, f.Codigo, f.Nombre, f.Tipo, f.Activo))
            .ToListAsync(ct);
    }

    public async Task<RelacionFormularioDto> GuardarAsync(SaveRelacionFormularioRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (req.OrigenId == req.DestinoId)
        { throw new InvalidOperationException("El formulario origen y destino no pueden ser el mismo."); }

        // Validar que ambos formularios existan dentro del tenant.
        var existenAmbos = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == req.OrigenId || f.Id == req.DestinoId)
            .CountAsync(ct);
        if (existenAmbos < 2) { throw new InvalidOperationException("Formulario origen o destino no existe."); }

        RelacionFormulario entity;
        if (req.Id is Guid id)
        {
            entity = await db.RelacionesFormulario.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Relacion no encontrada.");
        }
        else
        {
            // Evitar duplicados aunque el indice unique lo bloquearia.
            var dup = await db.RelacionesFormulario.AsNoTracking().AnyAsync(
                x => x.FormularioOrigenId == req.OrigenId && x.FormularioDestinoId == req.DestinoId, ct);
            if (dup) { throw new InvalidOperationException("Ya existe una relacion entre esos formularios."); }
            entity = new RelacionFormulario { TenantId = tid };
            db.RelacionesFormulario.Add(entity);
        }
        entity.FormularioOrigenId = req.OrigenId;
        entity.FormularioDestinoId = req.DestinoId;
        entity.TipoRelacion = string.IsNullOrWhiteSpace(req.TipoRelacion) ? null : req.TipoRelacion.Trim().ToUpperInvariant();
        entity.Activo = req.Activo;
        entity.Observacion = string.IsNullOrWhiteSpace(req.Observacion) ? null : req.Observacion.Trim();

        audit.Write(actor,
            req.Id is null ? "relacion_formulario.create" : "relacion_formulario.update",
            nameof(RelacionFormulario), entity.Id,
            previousValue: null,
            newValue: new { entity.FormularioOrigenId, entity.FormularioDestinoId, entity.Activo },
            tenantId: tid);

        await db.SaveChangesAsync(ct);

        var dto = (await ListarAsync(ct)).FirstOrDefault(x => x.Id == entity.Id);
        return dto ?? throw new InvalidOperationException("No se pudo recargar la relacion guardada.");
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.RelacionesFormulario.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }
        db.RelacionesFormulario.Remove(entity);
        audit.Write(actor, "relacion_formulario.delete", nameof(RelacionFormulario), entity.Id,
            previousValue: new { entity.FormularioOrigenId, entity.FormularioDestinoId }, newValue: null, tenantId: entity.TenantId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetActivoAsync(Guid id, bool activo, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.RelacionesFormulario.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }
        entity.Activo = activo;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
