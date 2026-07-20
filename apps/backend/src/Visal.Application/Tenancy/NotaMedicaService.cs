using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class NotaMedicaService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IConfiguracionClinicaService clinica) : INotaMedicaService
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

    public async Task<ValidarHcParaNotaResult> ValidarHcParaNotaAsync(
        Guid pacienteId, string? formatoCodigo, CancellationToken ct = default)
    {
        // Regla: el profesional no puede crear notas de un servicio si el paciente
        // no tiene una Historia Clinica del formato que pide el servicio, y si esa
        // historia no esta dentro de la ventana de validez configurada por la
        // empresa (Config Empresa > Validez de Historia Clinica en meses).

        // 1) Buscar la HC mas reciente NO inactiva del paciente que ademas matchee
        //    el formato exigido (por Codigo o CodigoSecundario del FormDefinition).
        //    Si el servicio no trae formato configurado, dejamos pasar — no podemos
        //    forzar una regla sin saber que formato exigir.
        if (string.IsNullOrWhiteSpace(formatoCodigo))
        {
            // Falta data en el servicio. Buscamos cualquier HC activa como fallback.
            var hcAny = await db.HistoriasClinicas.AsNoTracking()
                .Where(h => h.PacienteId == pacienteId && h.Estado != HistoriaClinicaEstado.Inactiva)
                .OrderByDescending(h => h.FechaApertura)
                .FirstOrDefaultAsync(ct);
            if (hcAny is null)
            {
                return new ValidarHcParaNotaResult(false,
                    "El paciente no tiene historia clinica creada. Crea una historia clinica antes de registrar notas.",
                    null);
            }
            return await VerificarVigenciaAsync(hcAny, ct);
        }

        var codigo = formatoCodigo.Trim();
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId && h.Estado != HistoriaClinicaEstado.Inactiva)
            .Join(db.FormDefinitions.AsNoTracking(),
                  h => h.FormDefinitionId, f => f.Id,
                  (h, f) => new { h, f })
            .Where(x => x.f.Codigo == codigo || x.f.CodigoSecundario == codigo)
            .OrderByDescending(x => x.h.FechaApertura)
            .Select(x => x.h)
            .FirstOrDefaultAsync(ct);

        if (hc is null)
        {
            return new ValidarHcParaNotaResult(false,
                $"El servicio requiere una historia clinica con formato '{codigo}'. " +
                "El paciente no tiene una. Crea la historia clinica primero desde el modulo Historia Medica.",
                null);
        }

        return await VerificarVigenciaAsync(hc, ct);
    }

    private async Task<ValidarHcParaNotaResult> VerificarVigenciaAsync(
        HistoriaClinica hc, CancellationToken ct)
    {
        var meses = await clinica.GetMesesValidezHistoriaClinicaAsync(ct);
        if (meses <= 0) { return new ValidarHcParaNotaResult(true, "OK", hc.Id); }
        var corte = DateTimeOffset.UtcNow.AddMonths(-meses);
        if (hc.FechaApertura < corte)
        {
            var antig = (DateTimeOffset.UtcNow - hc.FechaApertura).TotalDays / 30.0;
            return new ValidarHcParaNotaResult(false,
                $"La historia clinica del paciente esta vencida (abierta hace {antig:N1} meses, " +
                $"validez configurada: {meses} mes(es)). Crea una historia clinica nueva antes de registrar notas.",
                hc.Id);
        }
        return new ValidarHcParaNotaResult(true, "OK", hc.Id);
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
            // Defensa en backend: aunque la UI ya valida al abrir el modal, comprobamos
            // de nuevo aqui que la HC objetivo este vigente. Esto bloquea el guardado si
            // alguien intenta postear sin pasar por el flujo. Solo aplica al crear.
            var hcCheck = await db.HistoriasClinicas.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == req.HistoriaClinicaId, ct);
            if (hcCheck is null)
            {
                throw new InvalidOperationException(
                    "La historia clinica de destino no existe. Crea una historia clinica antes de registrar notas.");
            }
            if (hcCheck.Estado == HistoriaClinicaEstado.Inactiva)
            {
                throw new InvalidOperationException(
                    "La historia clinica esta inactiva. No se pueden agregar notas a una HC inactiva.");
            }
            var meses = await clinica.GetMesesValidezHistoriaClinicaAsync(ct);
            if (meses > 0 && hcCheck.FechaApertura < DateTimeOffset.UtcNow.AddMonths(-meses))
            {
                throw new InvalidOperationException(
                    $"La historia clinica esta vencida (validez: {meses} mes(es)). Crea una nueva HC antes de registrar notas.");
            }
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
        entity.FirmaPacienteDataUrl = string.IsNullOrWhiteSpace(req.FirmaPacienteDataUrl) ? entity.FirmaPacienteDataUrl : req.FirmaPacienteDataUrl;
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

    public async Task<IReadOnlyList<DocumentoPacienteDto>> ListarDocumentosPorPacienteAsync(
        Guid pacienteId, CancellationToken ct = default)
    {
        // EF Core 9 NO traduce .ToString() sobre un enum dentro de la proyeccion
        // (igual que el caso de Escalas en task #135). Materializamos primero los
        // datos primitivos + el enum tal cual, despues proyectamos al DTO en memoria.
        // LEFT JOIN via SelectMany + DefaultIfEmpty para incluir documentos "libres"
        // que no tienen NotaMedicaId (ej. firmas remotas capturadas desde el chat
        // WhatsApp del paciente sin nota especifica).
        var raw = await db.NotaMedicaDocumentos.AsNoTracking()
            .Where(d => d.PacienteId == pacienteId)
            .GroupJoin(db.NotasMedicas.AsNoTracking(),
                  d => d.NotaMedicaId,
                  n => n.Id,
                  (d, ns) => new { d, ns })
            .SelectMany(x => x.ns.DefaultIfEmpty(),
                        (x, n) => new
                        {
                            x.d.Id, x.d.NotaMedicaId, x.d.NombreOriginal, x.d.RutaArchivo,
                            x.d.TipoMime, x.d.Tamano, x.d.Categoria, x.d.TipoTerapia, x.d.Mes,
                            x.d.Anotaciones, x.d.CreatedAt,
                            FechaNota = (DateOnly?)(n != null ? n.FechaNota : null),
                            CodigoUnico = n != null ? n.CodigoUnico : null,
                            EstadoNota = n != null ? (int?)n.Estado : null
                        })
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return raw
            .Select(x => new DocumentoPacienteDto(
                x.Id, x.NotaMedicaId, x.NombreOriginal, x.RutaArchivo,
                x.TipoMime, x.Tamano, x.Categoria, x.TipoTerapia, x.Mes,
                x.Anotaciones, x.CreatedAt,
                x.FechaNota, x.CodigoUnico,
                x.EstadoNota.HasValue ? ((Visal.Domain.Entities.NotaMedicaEstado)x.EstadoNota.Value).ToString() : null))
            .ToList();
    }

    public async Task<NotaDocumentoDto> AdjuntarDocumentoAsync(
        AdjuntarDocumentoRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        // Resolver PacienteId desde la nota para que el documento quede asociado
        // tambien al paciente (visible desde el tab Documentos de Admision).
        var pacienteId = await db.NotasMedicas.AsNoTracking()
            .Where(n => n.Id == req.NotaMedicaId)
            .Select(n => n.PacienteId)
            .FirstOrDefaultAsync(ct);
        if (pacienteId == Guid.Empty) { throw new InvalidOperationException("Nota medica no encontrada."); }
        var entity = new NotaMedicaDocumento
        {
            TenantId = tid,
            NotaMedicaId = req.NotaMedicaId,
            PacienteId = pacienteId,
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

    public async Task<IReadOnlyList<NotaDocumentoDto>> ListarDocumentosPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default)
    {
        return await db.NotaMedicaDocumentos.AsNoTracking()
            .Where(d => d.HistoriaClinicaId == historiaId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new NotaDocumentoDto(
                d.Id, d.NotaMedicaId, d.NombreOriginal, d.RutaArchivo,
                d.TipoMime, d.Tamano, d.Categoria, d.TipoTerapia, d.Mes,
                d.Anotaciones, d.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<NotaDocumentoDto> AdjuntarDocumentoHcAsync(
        AdjuntarDocumentoHcRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        // Valida que la HC exista y pertenezca al paciente. La validacion cruzada
        // evita que un actor con acceso al tenant pueda enlazar un documento a una
        // HC de otro paciente pasando IDs arbitrarios.
        var hcExiste = await db.HistoriasClinicas.AsNoTracking()
            .AnyAsync(h => h.Id == req.HistoriaClinicaId && h.PacienteId == req.PacienteId, ct);
        if (!hcExiste) { throw new InvalidOperationException("Historia clinica no encontrada o no coincide con el paciente."); }
        var entity = new NotaMedicaDocumento
        {
            TenantId = tid,
            NotaMedicaId = null,
            HistoriaClinicaId = req.HistoriaClinicaId,
            PacienteId = req.PacienteId,
            NombreOriginal = req.NombreOriginal,
            RutaArchivo = req.RutaArchivo,
            TipoMime = req.TipoMime,
            Tamano = req.Tamano,
            Categoria = req.Categoria,
            Anotaciones = req.Anotaciones
        };
        db.NotaMedicaDocumentos.Add(entity);
        await db.SaveChangesAsync(ct);
        return new NotaDocumentoDto(
            entity.Id, entity.NotaMedicaId, entity.NombreOriginal, entity.RutaArchivo,
            entity.TipoMime, entity.Tamano, entity.Categoria, entity.TipoTerapia, entity.Mes,
            entity.Anotaciones, entity.CreatedAt);
    }

    public async Task<NotaDocumentoDto> AdjuntarDocumentoPacienteAsync(
        AdjuntarDocumentoPacienteRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        // Verifica que el paciente exista y este en el tenant activo (evita
        // pasar un PacienteId arbitrario de otro tenant).
        var pacienteExiste = await db.Pacientes.AsNoTracking()
            .AnyAsync(p => p.Id == req.PacienteId, ct);
        if (!pacienteExiste) { throw new InvalidOperationException("Paciente no encontrado."); }
        var entity = new NotaMedicaDocumento
        {
            TenantId = tid,
            NotaMedicaId = null,
            HistoriaClinicaId = null,
            PacienteId = req.PacienteId,
            NombreOriginal = req.NombreOriginal,
            RutaArchivo = req.RutaArchivo,
            TipoMime = req.TipoMime,
            Tamano = req.Tamano,
            Categoria = req.Categoria,
            Anotaciones = req.Anotaciones
        };
        db.NotaMedicaDocumentos.Add(entity);
        await db.SaveChangesAsync(ct);
        return new NotaDocumentoDto(
            entity.Id, entity.NotaMedicaId, entity.NombreOriginal, entity.RutaArchivo,
            entity.TipoMime, entity.Tamano, entity.Categoria, entity.TipoTerapia, entity.Mes,
            entity.Anotaciones, entity.CreatedAt);
    }

    public async Task<bool> ActualizarCategoriaDocumentoAsync(
        Guid documentoId, string? categoria, Guid actor, CancellationToken ct = default)
    {
        var e = await db.NotaMedicaDocumentos.FirstOrDefaultAsync(d => d.Id == documentoId, ct);
        if (e is null) { return false; }
        var nueva = string.IsNullOrWhiteSpace(categoria) ? null : categoria.Trim();
        if (e.Categoria == nueva) { return true; }
        e.Categoria = nueva;
        await db.SaveChangesAsync(ct);
        return true;
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
        n.FirmaDataUrl, n.FirmaPacienteDataUrl, n.CreatedAt);

    private static NotaMedicaEstado ParseEstado(string? s) =>
        Enum.TryParse<NotaMedicaEstado>(s, true, out var v) ? v : NotaMedicaEstado.Parcial;

    private static NotaMedicaCriticidad ParseCriticidad(string? s) =>
        Enum.TryParse<NotaMedicaCriticidad>(s, true, out var v) ? v : NotaMedicaCriticidad.Estable;
}
