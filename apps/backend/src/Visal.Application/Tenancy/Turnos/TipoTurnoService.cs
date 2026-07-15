using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Turnos;

public sealed class TipoTurnoService(IApplicationDbContext db, ITenantContext tenant) : ITipoTurnoService
{
    // #RRGGBB o #RRGGBBAA. Se valida antes de persistir para no ensuciar el catalogo.
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<TipoTurnoDto>> ListarAsync(bool incluirInactivos = false, CancellationToken ct = default)
    {
        var q = db.TiposTurno.AsNoTracking();
        if (!incluirInactivos) { q = q.Where(t => t.Activo); }
        var rows = await q.OrderBy(t => t.Orden).ThenBy(t => t.Codigo).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<TipoTurnoDto> GuardarAsync(GuardarTipoTurnoCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        Validar(cmd);

        var codigo = cmd.Codigo.Trim().ToUpperInvariant();

        // Unicidad de codigo por tenant, respetando el propio Id al editar.
        var duplicado = await db.TiposTurno
            .Where(t => t.Codigo == codigo && (cmd.Id == null || t.Id != cmd.Id))
            .AnyAsync(ct);
        if (duplicado) { throw new InvalidOperationException($"Ya existe un tipo con codigo '{codigo}' en este tenant."); }

        TipoTurno entity;
        if (cmd.Id is Guid id)
        {
            entity = await db.TiposTurno.FirstOrDefaultAsync(t => t.Id == id, ct)
                ?? throw new InvalidOperationException("Tipo de turno no encontrado.");
            entity.Codigo = codigo;
            entity.Etiqueta = cmd.Etiqueta.Trim();
            entity.HorasDefault = cmd.HorasDefault;
            entity.ColorFondo = cmd.ColorFondo.Trim();
            entity.ColorTexto = cmd.ColorTexto.Trim();
            entity.ColorBorde = cmd.ColorBorde.Trim();
            entity.Orden = cmd.Orden;
            entity.Activo = cmd.Activo;
        }
        else
        {
            entity = new TipoTurno
            {
                Id = Guid.CreateVersion7(),
                TenantId = tid,
                Codigo = codigo,
                Etiqueta = cmd.Etiqueta.Trim(),
                HorasDefault = cmd.HorasDefault,
                ColorFondo = cmd.ColorFondo.Trim(),
                ColorTexto = cmd.ColorTexto.Trim(),
                ColorBorde = cmd.ColorBorde.Trim(),
                Orden = cmd.Orden,
                Activo = cmd.Activo
            };
            db.TiposTurno.Add(entity);
        }
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var entity = await db.TiposTurno.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) { return false; }
        db.TiposTurno.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Validar(GuardarTipoTurnoCmd cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Codigo)) { throw new InvalidOperationException("El codigo es obligatorio."); }
        if (cmd.Codigo.Trim().Length > 8) { throw new InvalidOperationException("El codigo no puede exceder 8 caracteres."); }
        if (string.IsNullOrWhiteSpace(cmd.Etiqueta)) { throw new InvalidOperationException("La etiqueta es obligatoria."); }
        if (cmd.Etiqueta.Trim().Length > 40) { throw new InvalidOperationException("La etiqueta no puede exceder 40 caracteres."); }
        if (cmd.HorasDefault < 0m || cmd.HorasDefault > 24m)
        { throw new InvalidOperationException("Las horas deben estar entre 0 y 24."); }
        if (!HexColor.IsMatch(cmd.ColorFondo ?? "")) { throw new InvalidOperationException("ColorFondo debe ser #RRGGBB o #RRGGBBAA."); }
        if (!HexColor.IsMatch(cmd.ColorTexto ?? "")) { throw new InvalidOperationException("ColorTexto debe ser #RRGGBB o #RRGGBBAA."); }
        if (!HexColor.IsMatch(cmd.ColorBorde ?? "")) { throw new InvalidOperationException("ColorBorde debe ser #RRGGBB o #RRGGBBAA."); }
    }

    private static TipoTurnoDto ToDto(TipoTurno t) =>
        new(t.Id, t.Codigo, t.Etiqueta, t.HorasDefault, t.ColorFondo, t.ColorTexto, t.ColorBorde, t.Orden, t.Activo);
}
