using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Auth;

/// <summary>
/// Implementacion de autogestion. Los cambios de PlatformUser son globales
/// (afectan al usuario en todos los tenants) pero los datos del profesional
/// vinculado se resuelven contra el tenant activo. Un PlatformUser puede
/// tener varios TenantUsers — resolvemos por el tenant del contexto.
/// </summary>
public sealed class MiPerfilService(IApplicationDbContext db, ITenantContext tenant) : IMiPerfilService
{
    public async Task<MiPerfilDto?> GetAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var pu = await db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == platformUserId, ct);
        if (pu is null) { return null; }

        ProfesionalVinculadoDto? profVinculado = null;
        if (tenant.TenantId is Guid tid)
        {
            // Buscamos el TenantUser del usuario en el tenant activo; si esta
            // vinculado a un Profesional, cargamos su snapshot readonly.
            var tu = await db.TenantUsers.IgnoreQueryFilters()
                .Where(t => t.PlatformUserId == platformUserId && t.TenantId == tid)
                .FirstOrDefaultAsync(ct);
            if (tu?.ProfesionalId is Guid pid)
            {
                var prof = await db.Profesionales.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == pid, ct);
                if (prof is not null)
                {
                    var tipo = prof.TipoProfesionalId is Guid tpid
                        ? await db.TiposProfesional.AsNoTracking()
                            .Where(t => t.Id == tpid).Select(t => t.Nombre).FirstOrDefaultAsync(ct)
                        : null;
                    var subs = await db.ProfesionalSubCategorias.AsNoTracking()
                        .Where(s => s.ProfesionalId == pid)
                        .Join(db.SubCategoriasProfesional.AsNoTracking(),
                              s => s.SubCategoriaId, sc => sc.Id, (s, sc) => sc.Nombre)
                        .ToListAsync(ct);
                    var sedes = await db.ProfesionalAgencias.AsNoTracking()
                        .Where(a => a.ProfesionalId == pid)
                        .Select(a => a.Agencia)
                        .ToListAsync(ct);
                    profVinculado = new ProfesionalVinculadoDto(
                        prof.Id, prof.NumeroDocumento, prof.TipoDocumento, prof.NombreCompleto,
                        tipo, prof.RegistroMedico, prof.Ciudad, prof.Celular, prof.FirmaUrl,
                        subs, sedes);
                }
            }
        }

        return new MiPerfilDto(
            pu.Id, pu.Email, pu.Username, pu.Documento,
            pu.DisplayName, pu.PrimerNombre, pu.SegundoNombre,
            pu.PrimerApellido, pu.SegundoApellido,
            pu.Celular, pu.Fijo, pu.Ciudad, pu.Direccion,
            pu.AvatarUrl,
            profVinculado);
    }

    public async Task<MiPerfilDto?> ActualizarPerfilAsync(
        Guid platformUserId, ActualizarMiPerfilRequest req, CancellationToken ct = default)
    {
        var pu = await db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == platformUserId, ct);
        if (pu is null) { return null; }

        // Email: si cambia, validar unico. Si el nuevo email ya existe en otro
        // usuario, no lo permitimos — el login por email dejaria de ser unico.
        var emailNorm = (req.Email ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(emailNorm) && emailNorm != pu.Email.ToLowerInvariant())
        {
            var choca = await db.PlatformUsers.IgnoreQueryFilters()
                .AnyAsync(u => u.Id != platformUserId && u.Email.ToLower() == emailNorm, ct);
            if (choca) { throw new InvalidOperationException("Ese correo ya esta en uso por otro usuario."); }
            pu.Email = emailNorm;
        }

        pu.PrimerNombre = Trim(req.PrimerNombre);
        pu.SegundoNombre = Trim(req.SegundoNombre);
        pu.PrimerApellido = Trim(req.PrimerApellido);
        pu.SegundoApellido = Trim(req.SegundoApellido);
        pu.Celular = Trim(req.Celular);
        pu.Fijo = Trim(req.Fijo);
        pu.Ciudad = Trim(req.Ciudad);
        pu.Direccion = Trim(req.Direccion);
        pu.AvatarUrl = Trim(req.AvatarUrl);

        // Sincronizar DisplayName con la concatenacion cuando el user llena los
        // subcampos. Si dejo todo vacio, respetamos el DisplayName anterior.
        var nombreCompleto = string.Join(' ', new[] {
            pu.PrimerNombre, pu.SegundoNombre, pu.PrimerApellido, pu.SegundoApellido
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(nombreCompleto))
        {
            pu.DisplayName = nombreCompleto;
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(platformUserId, ct);
    }

    public async Task<bool> ActualizarFirmaProfesionalAsync(
        Guid platformUserId, string firmaDataUrl, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return false; }
        if (string.IsNullOrWhiteSpace(firmaDataUrl)) { return false; }

        var tu = await db.TenantUsers.IgnoreQueryFilters()
            .Where(t => t.PlatformUserId == platformUserId && t.TenantId == tid)
            .FirstOrDefaultAsync(ct);
        if (tu?.ProfesionalId is not Guid pid) { return false; }

        var prof = await db.Profesionales.FirstOrDefaultAsync(p => p.Id == pid, ct);
        if (prof is null) { return false; }
        prof.FirmaUrl = firmaDataUrl;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
