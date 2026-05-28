using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class PacienteService : IPacienteService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public PacienteService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<PacienteDto>> ListAsync(string? filtro, CancellationToken ct = default)
    {
        var q = _db.Pacientes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = filtro.Trim().ToLower();
            q = q.Where(p => p.NombreCompleto.ToLower().Contains(f) || p.NumeroDocumento.ToLower().Contains(f));
        }
        return await q.OrderBy(p => p.NombreCompleto)
            .Select(p => new PacienteDto(p.Id, p.NumeroDocumento, p.NombreCompleto, p.Ciudad, p.Telefono,
                p.Aseguradora != null ? p.Aseguradora.Nombre : null))
            .ToListAsync(ct);
    }

    public async Task<PacienteDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Pacientes.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PacienteDetailDto(p.Id, p.NumeroDocumento, p.TipoDocumento, p.PrimerNombre, p.SegundoNombre,
                p.PrimerApellido, p.SegundoApellido, p.NombreCompleto, p.FechaNacimiento, p.Sexo, p.EstadoCivil,
                p.Telefono, p.Email, p.Direccion, p.Ciudad, p.Zona, p.Ocupacion, p.Regimen, p.AseguradoraId,
                p.ContactoEmergencia, p.Parentesco, p.TelefonoEmergencia, p.Activo))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PacienteDetailDto?> SaveAsync(SavePacienteRequest req, Guid actor, CancellationToken ct = default)
    {
        var doc = (req.NumeroDocumento ?? "").Trim();
        if (doc.Length == 0) { throw new InvalidOperationException("El numero de documento es obligatorio."); }
        var nombre = string.IsNullOrWhiteSpace(req.NombreCompleto)
            ? string.Join(' ', new[] { req.PrimerNombre, req.SegundoNombre, req.PrimerApellido, req.SegundoApellido }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim()
            : req.NombreCompleto.Trim();
        if (nombre.Length == 0) { nombre = doc; }

        Paciente p;
        if (req.Id is Guid id)
        {
            p = await _db.Pacientes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException("Paciente no encontrado.");
            if (await _db.Pacientes.AnyAsync(x => x.NumeroDocumento == doc && x.Id != id, ct)) { throw new InvalidOperationException($"Ya existe un paciente con documento '{doc}'."); }
        }
        else
        {
            if (_tenant.TenantId is not Guid tid) { return null; }
            if (await _db.Pacientes.AnyAsync(x => x.NumeroDocumento == doc, ct)) { throw new InvalidOperationException($"Ya existe un paciente con documento '{doc}'."); }
            p = new Paciente { TenantId = tid };
            _db.Pacientes.Add(p);
        }

        p.NumeroDocumento = doc;
        p.TipoDocumento = string.IsNullOrWhiteSpace(req.TipoDocumento) ? "CC" : req.TipoDocumento.Trim();
        p.PrimerNombre = req.PrimerNombre?.Trim();
        p.SegundoNombre = req.SegundoNombre?.Trim();
        p.PrimerApellido = req.PrimerApellido?.Trim();
        p.SegundoApellido = req.SegundoApellido?.Trim();
        p.NombreCompleto = nombre;
        p.FechaNacimiento = req.FechaNacimiento;
        p.Sexo = req.Sexo?.Trim();
        p.EstadoCivil = req.EstadoCivil?.Trim();
        p.Telefono = req.Telefono?.Trim();
        p.Email = req.Email?.Trim();
        p.Direccion = req.Direccion?.Trim();
        p.Ciudad = req.Ciudad?.Trim();
        p.Zona = req.Zona?.Trim();
        p.Ocupacion = req.Ocupacion?.Trim();
        p.Regimen = req.Regimen?.Trim();
        p.AseguradoraId = req.AseguradoraId;
        p.ContactoEmergencia = req.ContactoEmergencia?.Trim();
        p.Parentesco = req.Parentesco?.Trim();
        p.TelefonoEmergencia = req.TelefonoEmergencia?.Trim();
        p.Activo = req.Activo;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(p.Id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var p = await _db.Pacientes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return false; }
        _db.Pacientes.Remove(p);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
