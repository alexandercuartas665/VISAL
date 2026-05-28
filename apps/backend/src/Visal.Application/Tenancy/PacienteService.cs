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
            .Select(p => new PacienteDto(
                p.Id, p.NumeroDocumento, p.NombreCompleto, p.Ciudad, p.Telefono,
                p.Aseguradora != null ? p.Aseguradora.Nombre : null,
                p.SedeAtencion != null ? p.SedeAtencion.Nombre : null,
                p.Estado, p.FechaIngresoPad))
            .ToListAsync(ct);
    }

    public async Task<PacienteDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Pacientes.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PacienteDetailDto(
                p.Id, p.NumeroDocumento, p.TipoDocumento,
                p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido,
                p.NombreCompleto, p.FechaNacimiento, p.Edad,
                p.IpsComentaId, p.CodigoAceptacion, p.FechaComentan,
                p.AseguradoraId, p.FechaIngresoPad, p.FechaEgresoPad,
                p.DiasEstancia, p.OpIngresoDias,
                p.Incapacidad, p.GrupoRh, p.TipoUsuario, p.Estado,
                p.ClasificacionPaciente, p.ClasificacionGrupoPatologia,
                p.EstratoSocial, p.Sexo, p.EstadoCivil, p.Zona,
                p.Ocupacion, p.Regimen,
                p.Contrato1Id, p.Contrato2Id, p.Contrato3Id,
                p.Cie10Id, p.DiagnosticoPrincipal,
                p.Tutela, p.TipoTutela, p.MedContratado,
                p.PaisResidenciaId, p.PaisOrigenId, p.DepartamentoId, p.MunicipioId,
                p.Direccion, p.Barrio, p.Ciudad,
                p.Telefono, p.Email,
                p.SedeAtencionId,
                p.ContactoEmergencia, p.Parentesco, p.TelefonoEmergencia,
                p.Activo))
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

        // Identificacion
        p.NumeroDocumento = doc;
        p.TipoDocumento = string.IsNullOrWhiteSpace(req.TipoDocumento) ? "CC" : req.TipoDocumento.Trim();
        p.PrimerNombre = req.PrimerNombre?.Trim();
        p.SegundoNombre = req.SegundoNombre?.Trim();
        p.PrimerApellido = req.PrimerApellido?.Trim();
        p.SegundoApellido = req.SegundoApellido?.Trim();
        p.NombreCompleto = nombre;
        p.FechaNacimiento = req.FechaNacimiento;
        p.Edad = req.Edad ?? CalcularEdad(req.FechaNacimiento);

        // Admin PAD
        p.IpsComentaId = req.IpsComentaId;
        p.CodigoAceptacion = req.CodigoAceptacion?.Trim();
        p.FechaComentan = req.FechaComentan;
        p.AseguradoraId = req.AseguradoraId;
        p.FechaIngresoPad = req.FechaIngresoPad;
        p.FechaEgresoPad = req.FechaEgresoPad;
        p.DiasEstancia = req.DiasEstancia ?? CalcularDiasEstancia(req.FechaIngresoPad, req.FechaEgresoPad);
        p.OpIngresoDias = req.OpIngresoDias;

        // Clasificaciones
        p.Incapacidad = req.Incapacidad?.Trim();
        p.GrupoRh = req.GrupoRh?.Trim();
        p.TipoUsuario = req.TipoUsuario?.Trim();
        p.Estado = req.Estado?.Trim();
        p.ClasificacionPaciente = req.ClasificacionPaciente?.Trim();
        p.ClasificacionGrupoPatologia = req.ClasificacionGrupoPatologia?.Trim();
        p.EstratoSocial = req.EstratoSocial?.Trim();
        p.Sexo = req.Sexo?.Trim();
        p.EstadoCivil = req.EstadoCivil?.Trim();
        p.Zona = req.Zona?.Trim();
        p.Ocupacion = req.Ocupacion?.Trim();
        p.Regimen = req.Regimen?.Trim();

        // Contratos
        p.Contrato1Id = req.Contrato1Id;
        p.Contrato2Id = req.Contrato2Id;
        p.Contrato3Id = req.Contrato3Id;

        // Diagnostico
        p.Cie10Id = req.Cie10Id;
        p.DiagnosticoPrincipal = req.DiagnosticoPrincipal?.Trim();

        // Tutela
        p.Tutela = req.Tutela?.Trim();
        p.TipoTutela = req.TipoTutela?.Trim();
        p.MedContratado = req.MedContratado?.Trim();

        // Geografia
        p.PaisResidenciaId = req.PaisResidenciaId;
        p.PaisOrigenId = req.PaisOrigenId;
        p.DepartamentoId = req.DepartamentoId;
        p.MunicipioId = req.MunicipioId;
        p.Direccion = req.Direccion?.Trim();
        p.Barrio = req.Barrio?.Trim();
        p.Ciudad = req.Ciudad?.Trim();

        // Contacto
        p.Telefono = req.Telefono?.Trim();
        p.Email = req.Email?.Trim();

        // Sede
        p.SedeAtencionId = req.SedeAtencionId;

        // Emergencia
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

    private static int? CalcularEdad(DateOnly? fechaNacimiento)
    {
        if (fechaNacimiento is not DateOnly fn) { return null; }
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var edad = hoy.Year - fn.Year;
        if (fn > hoy.AddYears(-edad)) { edad--; }
        return edad < 0 || edad > 130 ? null : edad;
    }

    private static int? CalcularDiasEstancia(DateOnly? ingreso, DateOnly? egreso)
    {
        if (ingreso is not DateOnly i) { return null; }
        var fin = egreso ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var dias = fin.DayNumber - i.DayNumber;
        return dias >= 0 ? dias : null;
    }
}
