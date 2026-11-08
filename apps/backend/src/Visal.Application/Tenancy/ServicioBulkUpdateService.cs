using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class ServicioBulkUpdateService : IServicioBulkUpdateService
{
    private const int MaxHistorial = 20;
    private const int PreviewSize = 10;

    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenant;

    public ServicioBulkUpdateService(IApplicationDbContextFactory dbFactory, ITenantContext tenant)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
    }

    public async Task<BulkBusquedaResultado> BuscarAsync(OperadorBusquedaServicio operador, string texto, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var q = ConstruirFiltro(db, operador, texto);
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
                Contrato = db.ContratosAseguradora.Where(c => c.Id == s.ContratoId)
                    .Select(c => new { c.CodigoContrato, Aseguradora = db.Aseguradoras.Where(a => a.Id == c.AseguradoraId).Select(a => a.Nombre).FirstOrDefault() ?? "" })
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
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (string.IsNullOrWhiteSpace(req.Texto)) { return null; }
        if (string.IsNullOrWhiteSpace(req.Motivo)) { return null; }
        // Debe pisar al menos un campo; si los 3 son null la ejecucion no tiene efecto.
        if (req.NuevaModalidad is null && req.NuevoGrupoServicio is null && req.NuevoServicio is null) { return null; }

        var afectados = await ConstruirFiltro(db, req.Operador, req.Texto).ToListAsync(ct);
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
        db.ServicioBulkUpdates.Add(bulk);

        foreach (var s in afectados)
        {
            db.ServicioBulkUpdateItems.Add(new ServicioBulkUpdateItem
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

        await db.SaveChangesAsync(ct);
        await PurgarHistorialAsync(db, tid, ct);
        return MapDto(bulk);
    }

    public async Task<IReadOnlyList<BulkUpdateDto>> ListarHistorialAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        return await db.ServicioBulkUpdates.AsNoTracking()
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
        using var db = _dbFactory.Create();
        var bulk = await db.ServicioBulkUpdates.Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == bulkUpdateId, ct);
        if (bulk is null || bulk.Estado == "Revertida") { return false; }

        var ids = bulk.Items.Select(i => i.ServicioContratoId).ToList();
        var servicios = await db.ServiciosContrato.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
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
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Query filter over ServicioContrato.Descripcion. Case-insensitive via Lower().
    // Peligro (b): devuelve IQueryable sobre 'db' -> recibe el contexto del llamador y este
    // enumera el IQueryable (Count/ToList) DENTRO de su propio using (sin ObjectDisposedException).
    private IQueryable<ServicioContrato> ConstruirFiltro(IApplicationDbContext db, OperadorBusquedaServicio op, string texto)
    {
        var t = texto.Trim().ToLower();
        var q = db.ServiciosContrato.AsQueryable();
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
    // Peligro (a): helper privado que usa el contexto -> recibe el 'db' del llamador (misma
    // operacion logica = un solo contexto) para no abrir uno nuevo dentro de AplicarAsync.
    private async Task PurgarHistorialAsync(IApplicationDbContext db, Guid tid, CancellationToken ct)
    {
        var extras = await db.ServicioBulkUpdates
            .OrderByDescending(b => b.CreatedAt)
            .Skip(MaxHistorial)
            .ToListAsync(ct);
        if (extras.Count == 0) { return; }
        db.ServicioBulkUpdates.RemoveRange(extras);
        await db.SaveChangesAsync(ct);
    }

    private static BulkUpdateDto MapDto(ServicioBulkUpdate b) => new(
        b.Id, b.CreatedAt, "",
        b.OperadorBusqueda, b.TextoBusqueda,
        b.NuevaModalidadFacturacion, b.NuevoGrupoServicioFacturacion, b.NuevoServicioFacturacion,
        b.Motivo, b.TotalAfectados, b.Estado);
}
