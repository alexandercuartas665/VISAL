using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class ServicioBulkUpdateService : IServicioBulkUpdateService
{
    private const int MaxHistorial = 20;
    private const int PreviewSize = 10;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ServicioBulkUpdateService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<BulkBusquedaResultado> BuscarAsync(OperadorBusquedaServicio operador, string texto, CancellationToken ct = default)
    {
        var q = ConstruirFiltro(operador, texto);
        var total = await q.CountAsync(ct);
        // Preview con join a contrato+aseguradora — el usuario necesita ubicar
        // rapido a que EPS/contrato pertenece cada match. Limitado a 10 filas.
        var preview = await q.OrderBy(s => s.Descripcion)
            .Take(PreviewSize)
            .Select(s => new
            {
                s.Id,
                s.Descripcion,
                s.ModalidadFacturacion,
                s.GrupoServicioFacturacion,
                s.ServicioFacturacion,
                Contrato = _db.ContratosAseguradora.Where(c => c.Id == s.ContratoId)
                    .Select(c => new { c.CodigoContrato, Aseguradora = _db.Aseguradoras.Where(a => a.Id == c.AseguradoraId).Select(a => a.Nombre).FirstOrDefault() ?? "" })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var filas = preview.Select(p => new BulkPreviewFila(
            p.Id,
            p.Contrato?.Aseguradora ?? "",
            p.Contrato?.CodigoContrato ?? "",
            p.Descripcion,
            p.ModalidadFacturacion,
            p.GrupoServicioFacturacion,
            p.ServicioFacturacion)).ToList();

        return new BulkBusquedaResultado(total, filas);
    }

    public async Task<BulkUpdateDto?> AplicarAsync(AplicarBulkRequest req, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (string.IsNullOrWhiteSpace(req.Texto)) { return null; }
        if (string.IsNullOrWhiteSpace(req.Motivo)) { return null; }
        // Debe pisar al menos un campo; si los 3 son null la ejecucion no tiene efecto.
        if (req.NuevaModalidad is null && req.NuevoGrupoServicio is null && req.NuevoServicio is null) { return null; }

        var afectados = await ConstruirFiltro(req.Operador, req.Texto).ToListAsync(ct);
        if (afectados.Count == 0) { return null; }

        var bulk = new ServicioBulkUpdate
        {
            TenantId = tid,
            OperadorBusqueda = req.Operador.ToString(),
            TextoBusqueda = req.Texto.Trim(),
            NuevaModalidadFacturacion = req.NuevaModalidad?.Trim(),
            NuevoGrupoServicioFacturacion = req.NuevoGrupoServicio?.Trim(),
            NuevoServicioFacturacion = req.NuevoServicio?.Trim(),
            Motivo = req.Motivo.Trim(),
            TotalAfectados = afectados.Count,
            Estado = "Aplicada",
            CreatedBy = actor,
        };
        _db.ServicioBulkUpdates.Add(bulk);

        foreach (var s in afectados)
        {
            _db.ServicioBulkUpdateItems.Add(new ServicioBulkUpdateItem
            {
                TenantId = tid,
                BulkUpdate = bulk,
                ServicioContratoId = s.Id,
                ModalidadFacturacionAntes = s.ModalidadFacturacion,
                GrupoServicioFacturacionAntes = s.GrupoServicioFacturacion,
                ServicioFacturacionAntes = s.ServicioFacturacion,
                CreatedBy = actor,
            });
            if (req.NuevaModalidad is not null) { s.ModalidadFacturacion = req.NuevaModalidad.Trim(); }
            if (req.NuevoGrupoServicio is not null) { s.GrupoServicioFacturacion = req.NuevoGrupoServicio.Trim(); }
            if (req.NuevoServicio is not null) { s.ServicioFacturacion = req.NuevoServicio.Trim(); }
        }

        await _db.SaveChangesAsync(ct);
        await PurgarHistorialAsync(tid, ct);
        return MapDto(bulk);
    }

    public async Task<IReadOnlyList<BulkUpdateDto>> ListarHistorialAsync(CancellationToken ct = default)
    {
        return await _db.ServicioBulkUpdates.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Take(MaxHistorial)
            .Select(b => new BulkUpdateDto(
                b.Id, b.CreatedAt, "",
                b.OperadorBusqueda, b.TextoBusqueda,
                b.NuevaModalidadFacturacion, b.NuevoGrupoServicioFacturacion, b.NuevoServicioFacturacion,
                b.Motivo, b.TotalAfectados, b.Estado))
            .ToListAsync(ct);
    }

    public async Task<bool> RevertirAsync(Guid bulkUpdateId, Guid actor, CancellationToken ct = default)
    {
        var bulk = await _db.ServicioBulkUpdates.Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == bulkUpdateId, ct);
        if (bulk is null || bulk.Estado == "Revertida") { return false; }

        var ids = bulk.Items.Select(i => i.ServicioContratoId).ToList();
        var servicios = await _db.ServiciosContrato.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        var itemsPorId = bulk.Items.ToDictionary(i => i.ServicioContratoId);
        foreach (var s in servicios)
        {
            if (!itemsPorId.TryGetValue(s.Id, out var it)) { continue; }
            s.ModalidadFacturacion = it.ModalidadFacturacionAntes;
            s.GrupoServicioFacturacion = it.GrupoServicioFacturacionAntes;
            s.ServicioFacturacion = it.ServicioFacturacionAntes;
        }
        bulk.Estado = "Revertida";
        bulk.FechaReversion = DateTimeOffset.UtcNow;
        bulk.RevertidoPor = actor;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // Query filter over ServicioContrato.Descripcion. Case-insensitive via Lower().
    private IQueryable<ServicioContrato> ConstruirFiltro(OperadorBusquedaServicio op, string texto)
    {
        var t = texto.Trim().ToLower();
        var q = _db.ServiciosContrato.AsQueryable();
        return op switch
        {
            OperadorBusquedaServicio.EmpiezaCon => q.Where(s => s.Descripcion != null && s.Descripcion.ToLower().StartsWith(t)),
            OperadorBusquedaServicio.TerminaCon => q.Where(s => s.Descripcion != null && s.Descripcion.ToLower().EndsWith(t)),
            OperadorBusquedaServicio.Exacto     => q.Where(s => s.Descripcion != null && s.Descripcion.ToLower() == t),
            _                                   => q.Where(s => s.Descripcion != null && s.Descripcion.ToLower().Contains(t)),
        };
    }

    // Retencion FIFO: al insertar la ejecucion N+1, borra las que exceden MaxHistorial.
    // Los items en cascada se borran por FK ON DELETE CASCADE (ver EF config).
    private async Task PurgarHistorialAsync(Guid tid, CancellationToken ct)
    {
        var extras = await _db.ServicioBulkUpdates
            .OrderByDescending(b => b.CreatedAt)
            .Skip(MaxHistorial)
            .ToListAsync(ct);
        if (extras.Count == 0) { return; }
        _db.ServicioBulkUpdates.RemoveRange(extras);
        await _db.SaveChangesAsync(ct);
    }

    private static BulkUpdateDto MapDto(ServicioBulkUpdate b) => new(
        b.Id, b.CreatedAt, "",
        b.OperadorBusqueda, b.TextoBusqueda,
        b.NuevaModalidadFacturacion, b.NuevoGrupoServicioFacturacion, b.NuevoServicioFacturacion,
        b.Motivo, b.TotalAfectados, b.Estado);
}
