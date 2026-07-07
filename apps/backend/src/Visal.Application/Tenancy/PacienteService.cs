using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class PacienteService : IPacienteService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public PacienteService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
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
        var p = await _db.Pacientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return null; }
        var contactos = await _db.PacienteContactosEmergencia.AsNoTracking()
            .Where(c => c.PacienteId == id)
            .OrderBy(c => c.Orden).ThenBy(c => c.Nombre)
            .Select(c => new PacienteContactoEmergenciaDto(c.Id, c.Nombre, c.Parentesco, c.CodigoPais, c.Telefono, c.Orden, c.FirmaUrl))
            .ToListAsync(ct);
        return new PacienteDetailDto(
            p.Id, p.NumeroDocumento, p.TipoDocumento,
            p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido,
            p.NombreCompleto, p.FechaNacimiento, p.Edad,
            p.IpsComentaId, p.CodigoAceptacion, p.FechaComentan,
            p.AseguradoraId, p.FechaIngresoPad, p.FechaEgresoPad,
            p.DiasEstancia, p.OpIngresoDias,
            p.Incapacidad, p.GrupoRh, p.TipoUsuarioId, p.Estado,
            p.ClasificacionPacienteId, p.ClasificacionGrupoPatologiaId,
            p.EstratoSocial, p.Sexo, p.EstadoCivil, p.Zona,
            p.Ocupacion, p.Regimen,
            p.Contrato1Id, p.Contrato2Id, p.Contrato3Id,
            p.Cie10Id, p.Cie10Codigo, p.DiagnosticoPrincipal,
            p.Tutela, p.TipoTutelaId, p.MedContratadoId,
            p.PaisResidenciaId, p.PaisOrigenId, p.DepartamentoId, p.MunicipioId,
            p.Direccion, p.Barrio, p.Ciudad,
            p.CodigoPaisTelefono, p.Telefono, p.Email,
            p.SedeAtencionId,
            p.ContactoEmergencia, p.Parentesco, p.TelefonoEmergencia,
            contactos,
            p.Activo,
            p.EstadoAdmision);
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
        p.TipoUsuarioId = req.TipoUsuarioId;
        p.Estado = req.Estado?.Trim();
        p.ClasificacionPacienteId = req.ClasificacionPacienteId;
        p.ClasificacionGrupoPatologiaId = req.ClasificacionGrupoPatologiaId;
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
        p.Cie10Codigo = req.Cie10Codigo?.Trim();
        p.DiagnosticoPrincipal = req.DiagnosticoPrincipal?.Trim();

        // Tutela
        p.Tutela = req.Tutela?.Trim();
        p.TipoTutelaId = req.TipoTutelaId;
        p.MedContratadoId = req.MedContratadoId;

        // Geografia
        p.PaisResidenciaId = req.PaisResidenciaId;
        p.PaisOrigenId = req.PaisOrigenId;
        p.DepartamentoId = req.DepartamentoId;
        p.MunicipioId = req.MunicipioId;
        p.Direccion = req.Direccion?.Trim();
        p.Barrio = req.Barrio?.Trim();
        p.Ciudad = req.Ciudad?.Trim();

        // Contacto
        p.CodigoPaisTelefono = string.IsNullOrWhiteSpace(req.CodigoPaisTelefono) ? "+57" : req.CodigoPaisTelefono.Trim();
        p.Telefono = req.Telefono?.Trim();
        p.Email = req.Email?.Trim();

        // Sede
        p.SedeAtencionId = req.SedeAtencionId;

        // Emergencia (campos legacy: el primer contacto de la lista pisa estos)
        var primero = req.ContactosEmergencia.FirstOrDefault();
        if (primero is not null && !string.IsNullOrWhiteSpace(primero.Nombre))
        {
            p.ContactoEmergencia = primero.Nombre.Trim();
            p.Parentesco = primero.Parentesco?.Trim();
            p.TelefonoEmergencia = primero.Telefono?.Trim();
        }
        else
        {
            p.ContactoEmergencia = req.ContactoEmergencia?.Trim();
            p.Parentesco = req.Parentesco?.Trim();
            p.TelefonoEmergencia = req.TelefonoEmergencia?.Trim();
        }

        p.Activo = req.Activo;
        await _db.SaveChangesAsync(ct);

        // Sincronizar lista de contactos. Estrategia simple: borrar todos los
        // existentes y reescribir desde la request. Es seguro porque la entidad
        // no tiene FKs entrantes (no rompe datos clinicos).
        if (_tenant.TenantId is Guid tidSync)
        {
            var anteriores = await _db.PacienteContactosEmergencia
                .Where(c => c.PacienteId == p.Id)
                .ToListAsync(ct);
            if (anteriores.Count > 0) { _db.PacienteContactosEmergencia.RemoveRange(anteriores); }

            var nuevos = req.ContactosEmergencia
                .Where(c => !string.IsNullOrWhiteSpace(c.Nombre))
                .Select((c, i) => new PacienteContactoEmergencia
                {
                    TenantId = tidSync,
                    PacienteId = p.Id,
                    Nombre = c.Nombre.Trim(),
                    Parentesco = c.Parentesco?.Trim(),
                    CodigoPais = string.IsNullOrWhiteSpace(c.CodigoPais) ? "+57" : c.CodigoPais.Trim(),
                    Telefono = c.Telefono?.Trim(),
                    Orden = c.Orden > 0 ? c.Orden : i + 1,
                    FirmaUrl = string.IsNullOrWhiteSpace(c.FirmaUrl) ? null : c.FirmaUrl
                })
                .ToList();
            if (nuevos.Count > 0) { _db.PacienteContactosEmergencia.AddRange(nuevos); }
            await _db.SaveChangesAsync(ct);
        }

        return await GetAsync(p.Id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var p = await _db.Pacientes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return false; }

        // Las FK a pacientes en asignaciones / historias_clinicas / notas_medicas
        // estan en ON DELETE RESTRICT, asi que el hard delete revienta con un
        // error crudo de Postgres si el paciente ya tiene datos clinicos. Aqui
        // lo chequeamos antes y devolvemos un mensaje legible. Si necesita
        // desactivarlo, el usuario tiene el flag Activo en la UI.
        var hcs    = await _db.HistoriasClinicas.AsNoTracking().CountAsync(x => x.PacienteId == id, ct);
        var notas  = await _db.NotasMedicas.AsNoTracking().CountAsync(x => x.PacienteId == id, ct);
        var asigs  = await _db.Asignaciones.AsNoTracking().CountAsync(x => x.PacienteId == id, ct);
        if (hcs + notas + asigs > 0)
        {
            var partes = new List<string>();
            if (hcs   > 0) { partes.Add($"{hcs} historia(s) clinica(s)"); }
            if (notas > 0) { partes.Add($"{notas} nota(s) medica(s)"); }
            if (asigs > 0) { partes.Add($"{asigs} asignacion(es)"); }
            throw new InvalidOperationException(
                $"No se puede eliminar el paciente \"{p.NombreCompleto}\" porque tiene datos clinicos asociados: "
                + string.Join(", ", partes)
                + ". Usa el switch \"Activo\" para desactivarlo.");
        }

        _db.Pacientes.Remove(p);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DesactivarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var p = await _db.Pacientes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return false; }
        if (!p.Activo) { return true; }
        p.Activo = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> UpdateTelefonoAsync(Guid pacienteId, string telefono, Guid actor, CancellationToken ct = default)
    {
        var p = await _db.Pacientes.FirstOrDefaultAsync(x => x.Id == pacienteId, ct);
        if (p is null) { return null; }
        // Normalizamos a solo digitos (descartamos +, espacios, guiones).
        var digits = new string((telefono ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { return null; }

        // El telefono entrante viene como {codigoPais}{local}. Separamos para que
        // p.Telefono guarde solo los locales (multi-valor separados por "; ") y el
        // codigo pais viva en p.CodigoPaisTelefono. Evita corromper el formato
        // multi-telefono: "Actualizar" solo reemplaza el PRINCIPAL, preserva los demas.
        var codigoPais = (p.CodigoPaisTelefono ?? "+57").TrimStart('+');
        var local = digits;
        if (codigoPais.Length > 0 && local.StartsWith(codigoPais) && local.Length > codigoPais.Length)
        {
            local = local.Substring(codigoPais.Length);
        }
        if (local.Length == 0) { local = digits; }

        var existentes = PacienteTelefonoHelper.Enumerar(p.Telefono)
            .Select(t => new string(t.Where(char.IsDigit).ToArray()))
            .Where(t => t.Length > 0 && t != local)
            .ToList();
        var nuevaLista = new List<string> { local };
        nuevaLista.AddRange(existentes);
        p.Telefono = PacienteTelefonoHelper.Empaquetar(nuevaLista);

        await _db.SaveChangesAsync(ct);
        // Devolvemos {codigoPaisSinPlus}{local} para que el chat lo use como ContactPhone.
        return codigoPais + local;
    }

    public async Task<IReadOnlyList<PacienteContactoEmergenciaDto>> ListContactosEmergenciaAsync(Guid pacienteId, CancellationToken ct = default)
    {
        return await _db.PacienteContactosEmergencia.AsNoTracking()
            .Where(c => c.PacienteId == pacienteId)
            .OrderBy(c => c.Orden).ThenBy(c => c.Nombre)
            .Select(c => new PacienteContactoEmergenciaDto(
                c.Id, c.Nombre, c.Parentesco, c.CodigoPais, c.Telefono, c.Orden, c.FirmaUrl))
            .ToListAsync(ct);
    }

    public async Task<PacienteContactoEmergenciaDto?> UpsertContactoEmergenciaAsync(Guid pacienteId, PacienteContactoEmergenciaDto contacto, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var paciente = await _db.Pacientes.FirstOrDefaultAsync(p => p.Id == pacienteId, ct);
        if (paciente is null) { return null; }

        PacienteContactoEmergencia entidad;
        if (contacto.Id is Guid existingId)
        {
            entidad = await _db.PacienteContactosEmergencia
                .FirstOrDefaultAsync(c => c.Id == existingId && c.PacienteId == pacienteId, ct)
                ?? throw new InvalidOperationException("Contacto no encontrado.");
        }
        else
        {
            // Nuevo contacto: le damos el siguiente Orden libre.
            var siguienteOrden = 1 + (await _db.PacienteContactosEmergencia
                .Where(c => c.PacienteId == pacienteId)
                .Select(c => (int?)c.Orden)
                .MaxAsync(ct) ?? 0);
            entidad = new PacienteContactoEmergencia
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PacienteId = pacienteId,
                Orden = siguienteOrden
            };
            _db.PacienteContactosEmergencia.Add(entidad);
        }
        entidad.Nombre = (contacto.Nombre ?? string.Empty).Trim();
        entidad.Parentesco = string.IsNullOrWhiteSpace(contacto.Parentesco) ? null : contacto.Parentesco!.Trim();
        entidad.CodigoPais = string.IsNullOrWhiteSpace(contacto.CodigoPais) ? "+57" : contacto.CodigoPais!;
        entidad.Telefono = string.IsNullOrWhiteSpace(contacto.Telefono)
            ? null
            : new string(contacto.Telefono!.Where(char.IsDigit).ToArray());
        if (contacto.FirmaUrl is not null) { entidad.FirmaUrl = contacto.FirmaUrl; }
        await _db.SaveChangesAsync(ct);
        return new PacienteContactoEmergenciaDto(
            entidad.Id, entidad.Nombre, entidad.Parentesco, entidad.CodigoPais,
            entidad.Telefono, entidad.Orden, entidad.FirmaUrl);
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

    // =========================================================================
    // Cerrar / Reabrir admision del paciente
    // =========================================================================

    /// <summary>
    /// Valida los campos obligatorios y cambia el estado a Cerrado. Los campos
    /// EXCLUIDOS de la validacion son (por decision de negocio): DiasEstancia,
    /// OpIngresoDias, GrupoRh, Email. El resto (identificacion, fechas basicas,
    /// admin PAD, contactos, geografia, contactos de emergencia) es obligatorio
    /// para permitir el cierre. Un paciente Cerrado ya no se puede editar ni
    /// eliminar hasta que un admin lo reabra.
    /// </summary>
    public async Task<CerrarPacienteResult> CerrarAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var p = await _db.Pacientes
            .Include(x => x.ContactosEmergencia)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return new CerrarPacienteResult(false, new[] { "Paciente no existe" }); }

        var faltantes = ValidarCamposObligatorios(p);
        if (faltantes.Count > 0)
        {
            return new CerrarPacienteResult(false, faltantes);
        }

        var estadoPrev = p.EstadoAdmision;
        p.EstadoAdmision = "Cerrado";
        p.FechaCierreAdmision = DateTimeOffset.UtcNow;
        _audit.Write(actor, "paciente.cerrar", nameof(Paciente), p.Id,
            previousValue: new { estadoAdmision = estadoPrev },
            newValue: new { estadoAdmision = p.EstadoAdmision, fechaCierre = p.FechaCierreAdmision, documento = p.NumeroDocumento, nombre = p.NombreCompleto },
            tenantId: p.TenantId);
        await _db.SaveChangesAsync(ct);
        return new CerrarPacienteResult(true, Array.Empty<string>());
    }

    /// <summary>Vuelve el paciente a estado Abierto. Auditoria obligatoria: la
    /// reapertura permite editar y eliminar de nuevo — es una accion administrativa.</summary>
    public async Task<bool> ReabrirAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var p = await _db.Pacientes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return false; }
        if (!string.Equals(p.EstadoAdmision, "Cerrado", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Solo se puede reabrir un paciente que este Cerrado.");
        }
        var fechaCierrePrev = p.FechaCierreAdmision;
        p.EstadoAdmision = "Abierto";
        p.FechaCierreAdmision = null;
        _audit.Write(actor, "paciente.reabrir", nameof(Paciente), p.Id,
            previousValue: new { estadoAdmision = "Cerrado", fechaCierre = fechaCierrePrev },
            newValue: new { estadoAdmision = p.EstadoAdmision, documento = p.NumeroDocumento, nombre = p.NombreCompleto },
            tenantId: p.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Retorna la lista de campos obligatorios que NO estan diligenciados. Si
    /// esta vacia, el paciente esta listo para cerrarse. Los nombres devueltos
    /// son legibles para mostrarlos al usuario en el modal de error.
    /// EXCLUIDOS (opcionales): DiasEstancia, OpIngresoDias, GrupoRh, Email.
    /// </summary>
    private static List<string> ValidarCamposObligatorios(Paciente p)
    {
        var faltantes = new List<string>();
        // Identificacion
        if (string.IsNullOrWhiteSpace(p.NumeroDocumento)) { faltantes.Add("Numero de documento"); }
        if (string.IsNullOrWhiteSpace(p.TipoDocumento)) { faltantes.Add("Tipo de documento"); }
        if (string.IsNullOrWhiteSpace(p.NombreCompleto)) { faltantes.Add("Nombre completo"); }
        if (string.IsNullOrWhiteSpace(p.PrimerNombre)) { faltantes.Add("Primer nombre"); }
        if (string.IsNullOrWhiteSpace(p.PrimerApellido)) { faltantes.Add("Primer apellido"); }
        if (p.FechaNacimiento is null) { faltantes.Add("Fecha de nacimiento"); }
        if (string.IsNullOrWhiteSpace(p.Sexo)) { faltantes.Add("Sexo"); }
        if (string.IsNullOrWhiteSpace(p.EstadoCivil)) { faltantes.Add("Estado civil"); }
        // Admin PAD (IPS que remite / codigo aceptacion / fechas / aseguradora / diagnostico)
        if (p.IpsComentaId is null) { faltantes.Add("IPS que comenta"); }
        if (string.IsNullOrWhiteSpace(p.CodigoAceptacion)) { faltantes.Add("Codigo de aceptacion"); }
        if (p.FechaComentan is null) { faltantes.Add("Fecha comentan"); }
        if (p.AseguradoraId is null) { faltantes.Add("Aseguradora"); }
        if (p.FechaIngresoPad is null) { faltantes.Add("Fecha ingreso PAD"); }
        if (p.Contrato1Id is null) { faltantes.Add("Contrato 1"); }
        if (string.IsNullOrWhiteSpace(p.Cie10Codigo)) { faltantes.Add("Codigo CIE-10 / diagnostico"); }
        if (string.IsNullOrWhiteSpace(p.DiagnosticoPrincipal)) { faltantes.Add("Diagnostico principal"); }
        // Clasificaciones
        if (p.TipoUsuarioId is null) { faltantes.Add("Tipo de usuario"); }
        if (string.IsNullOrWhiteSpace(p.Estado)) { faltantes.Add("Estado clinico"); }
        if (string.IsNullOrWhiteSpace(p.Regimen)) { faltantes.Add("Regimen"); }
        // Geografia
        if (p.PaisResidenciaId is null) { faltantes.Add("Pais de residencia"); }
        if (p.DepartamentoId is null) { faltantes.Add("Departamento"); }
        if (p.MunicipioId is null) { faltantes.Add("Municipio"); }
        if (string.IsNullOrWhiteSpace(p.Direccion)) { faltantes.Add("Direccion"); }
        // Contacto (Telefono obligatorio; Email opcional)
        if (string.IsNullOrWhiteSpace(p.Telefono)) { faltantes.Add("Telefono"); }
        // Sede
        if (p.SedeAtencionId is null) { faltantes.Add("Sede de atencion"); }
        // Contacto de emergencia: al menos uno con nombre + parentesco + telefono
        var tieneEmergencia = !string.IsNullOrWhiteSpace(p.ContactoEmergencia)
                           && !string.IsNullOrWhiteSpace(p.Parentesco)
                           && !string.IsNullOrWhiteSpace(p.TelefonoEmergencia);
        if (!tieneEmergencia && p.ContactosEmergencia.Count == 0)
        {
            faltantes.Add("Contacto de emergencia (nombre, parentesco y telefono)");
        }
        return faltantes;
    }
}
