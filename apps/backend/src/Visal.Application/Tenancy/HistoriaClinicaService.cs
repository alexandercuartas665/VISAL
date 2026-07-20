using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision;
using Visal.Application.Revision.Ia;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class HistoriaClinicaService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IAuditWriter audit,
    IRevisionPolicyService revPolicy,
    IRevisionKanbanService kanban,
    IPreRevisionIaQueue preRevisionQueue,
    IPreRevisionIaPendingStore preRevisionStore) : IHistoriaClinicaService
{
    public async Task<IReadOnlyList<HistoriaClinicaResumenDto>> ListarPorPacienteAsync(
        Guid pacienteId,
        DateOnly? desde = null, DateOnly? hasta = null,
        Guid? formDefinitionId = null,
        CancellationToken ct = default)
    {
        var q = db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId);

        if (desde is DateOnly d)
        {
            var dStart = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(h => h.FechaApertura >= dStart);
        }
        if (hasta is DateOnly h2)
        {
            var dEnd = new DateTimeOffset(h2.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            q = q.Where(h => h.FechaApertura <= dEnd);
        }
        if (formDefinitionId is Guid fid)
        {
            q = q.Where(h => h.FormDefinitionId == fid);
        }

        var rows = await q
            .Join(db.FormDefinitions.AsNoTracking(), h => h.FormDefinitionId, f => f.Id, (h, f) => new { h, f })
            .OrderByDescending(x => x.h.FechaApertura)
            .Take(200)
            .Select(x => new HistoriaClinicaResumenDto(
                x.h.Id, x.f.Id, x.f.Codigo, x.f.Nombre,
                x.h.Estado.ToString(), x.h.FechaApertura, x.h.FechaCierre,
                x.h.EspecialistaNombre, x.h.MotivoInactivacion, x.h.ProfesionalId))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<HistoriaClinicaDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == id)
            .Join(db.FormDefinitions.AsNoTracking(), h => h.FormDefinitionId, f => f.Id, (h, f) => new { h, f })
            .Select(x => new HistoriaClinicaDetailDto(
                x.h.Id, x.h.PacienteId, x.f.Id, x.f.Codigo, x.f.Nombre, x.f.Version,
                x.f.SchemaJson, x.f.PrefillRoutesJson, x.h.ValoresJson,
                x.h.Estado.ToString(), x.h.FechaApertura, x.h.FechaCierre,
                x.h.EspecialistaNombre, x.h.MotivoInactivacion, x.h.ProfesionalId,
                x.h.RipsViaIngresoCodigo, x.h.RipsViaIngresoNombre,
                x.h.RipsFinalidadCodigo, x.h.RipsFinalidadNombre,
                x.h.RipsCausaExternaCodigo, x.h.RipsCausaExternaNombre))
            .FirstOrDefaultAsync(ct);
        return row;
    }

    public async Task<HistoriaClinicaDetailDto> CrearAsync(CrearHistoriaRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        var formato = await db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == req.FormDefinitionId, ct)
            ?? throw new InvalidOperationException("Formato de historia no encontrado.");

        var paciente = await db.Pacientes.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.PacienteId, ct)
            ?? throw new InvalidOperationException("Paciente no encontrado.");

        // Validacion de RIPS: la Res. 202/2021 exige via + finalidad + causa externa
        // como datos obligatorios para reportar. Bloqueamos aqui para que ninguna HC
        // se persista sin ellos.
        if (string.IsNullOrWhiteSpace(req.RipsViaIngresoCodigo)
            || string.IsNullOrWhiteSpace(req.RipsFinalidadCodigo)
            || string.IsNullOrWhiteSpace(req.RipsCausaExternaCodigo))
        {
            throw new InvalidOperationException(
                "Debes indicar Via de ingreso, Finalidad de la consulta y Causa externa (datos RIPS obligatorios).");
        }

        var entity = new HistoriaClinica
        {
            TenantId = tid,
            PacienteId = req.PacienteId,
            FormDefinitionId = req.FormDefinitionId,
            ValoresJson = string.IsNullOrWhiteSpace(req.ValoresJson) ? "{}" : req.ValoresJson,
            Estado = HistoriaClinicaEstado.Abierta,
            FechaApertura = DateTimeOffset.UtcNow,
            EspecialistaNombre = req.EspecialistaNombre,
            ProfesionalId = req.ProfesionalId,
            RipsViaIngresoCodigo = req.RipsViaIngresoCodigo,
            RipsViaIngresoNombre = req.RipsViaIngresoNombre,
            RipsFinalidadCodigo = req.RipsFinalidadCodigo,
            RipsFinalidadNombre = req.RipsFinalidadNombre,
            RipsCausaExternaCodigo = req.RipsCausaExternaCodigo,
            RipsCausaExternaNombre = req.RipsCausaExternaNombre
        };
        db.HistoriasClinicas.Add(entity);
        await db.SaveChangesAsync(ct);

        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task<bool> GuardarValoresAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado != HistoriaClinicaEstado.Abierta)
        {
            throw new InvalidOperationException("Solo se pueden actualizar valores de historias abiertas.");
        }
        e.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? "{}" : valoresJson;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Guard reutilizable para los servicios de items (medicamentos,
    /// insumos, remisiones, RX, lab, etc.). AsNoTracking porque solo lee el
    /// estado; no debe interferir con entidades trackeadas del mismo DbContext
    /// scoped del circuito Blazor.</summary>
    public async Task<bool> EsAbiertaAsync(Guid historiaClinicaId, CancellationToken ct = default)
    {
        return await db.HistoriasClinicas
            .AsNoTracking()
            .Where(h => h.Id == historiaClinicaId && h.Estado == HistoriaClinicaEstado.Abierta)
            .AnyAsync(ct);
    }

    public async Task<bool> CerrarAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado == HistoriaClinicaEstado.Inactiva)
        {
            throw new InvalidOperationException("No se puede cerrar una historia inactiva.");
        }
        var estadoPrev = e.Estado;
        e.ValoresJson = string.IsNullOrWhiteSpace(valoresJson) ? e.ValoresJson : valoresJson;
        e.Estado = HistoriaClinicaEstado.Cerrada;
        e.FechaCierre = DateTimeOffset.UtcNow;
        // Auditoria antes de SaveChanges: audit.Write solo agrega la entrada al
        // DbContext; los cambios se persisten en el mismo SaveChangesAsync que
        // guarda la HC, garantizando atomicidad (mismo patron que el resto del
        // codebase — ver FormDefinitionService, WhatsAppLineService, etc.).
        audit.Write(actor, "historia-clinica.cerrar", nameof(HistoriaClinica), e.Id,
            previousValue: new { estado = estadoPrev.ToString() },
            newValue: new { estado = e.Estado.ToString(), fechaCierre = e.FechaCierre, pacienteId = e.PacienteId, formDefinitionId = e.FormDefinitionId, especialista = e.EspecialistaNombre },
            tenantId: e.TenantId);
        await db.SaveChangesAsync(ct);

        // Capa 08 Ola 4 — trigger automatico del ciclo de revision al cerrar la HC.
        // Solo si el tenant tiene `AutoTriggerCierre = true` en `RevisionPolicy`.
        // El default es false, asi que ningun tenant existente ve cambios sin haberlo
        // activado explicitamente. El boton "Enviar a revision" en el modal HC sigue
        // como fallback manual — SolicitarSiFaltaAsync es idempotente.
        //
        // Si el trigger falla (BD, tenant sin policy, servicio caido), NO revertimos
        // el cierre — la HC ya quedo cerrada y el motivo clinico es prioritario.
        // Se registra en auditoria como evento aparte para que el operador pueda
        // reintentarlo manualmente desde el modal HC.
        try
        {
            var policy = await revPolicy.GetAsync(ct);
            if (policy.AutoTriggerCierre)
            {
                var rev = await kanban.SolicitarSiFaltaAsync(e.Id, actor, ct);

                // Capa 08 Ola 5 — trigger automatico IA. Se ejecuta solo si el operador
                // encendio `PreRevisionIAAutoTrigger` en la policy.
                //
                // Ola 8 RC8e — encolamos en vez de ejecutar sincrono. El worker
                // consume del channel y ejecuta el orquestador en su scope propio;
                // asi el usuario recupera el control apenas la HC persistio, sin
                // esperar al proveedor de IA (que puede tardar segundos por retry).
                // El worker maneja sus propios errores; si el channel falla al
                // encolar (imposible en unbounded, pero por robustez) el cierre
                // sigue OK y solo pierde la pre-revision automatica.
                if (policy.PreRevisionIAAutoTrigger)
                {
                    try
                    {
                        // Ola 9 RC9c — persistimos primero en la staging table.
                        // Si el proceso muere entre INSERT y el WriteAsync del
                        // channel, el startup del worker relee la tabla y
                        // reencola. Si el proceso muere despues del Write pero
                        // antes de que el worker consuma, tambien: la fila
                        // sigue viva hasta que el worker haga Delete al terminar.
                        var job = new PreRevisionIaJob(e.TenantId, rev.Id, actor);
                        var pendingId = await preRevisionStore.InsertAsync(job, ct);
                        await preRevisionQueue.EnqueueAsync(job with { PendingId = pendingId }, ct);
                    }
                    catch (Exception qEx)
                    {
                        audit.Write(actor, "historia-clinica.prerevision-ia-queue-fail", nameof(HistoriaClinica), e.Id,
                            previousValue: null,
                            newValue: new { revisionId = rev.Id, error = qEx.Message, exceptionType = qEx.GetType().Name },
                            tenantId: e.TenantId);
                        await db.SaveChangesAsync(ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            audit.Write(actor, "historia-clinica.trigger-revision-fail", nameof(HistoriaClinica), e.Id,
                previousValue: null,
                newValue: new { error = ex.Message, exceptionType = ex.GetType().Name },
                tenantId: e.TenantId);
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task<bool> ReabrirAsync(Guid id, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado != HistoriaClinicaEstado.Cerrada)
        {
            throw new InvalidOperationException("Solo se puede reabrir una historia que este Cerrada.");
        }
        var fechaCierrePrev = e.FechaCierre;
        e.Estado = HistoriaClinicaEstado.Abierta;
        e.FechaCierre = null;
        // Auditoria critica: reabrir una HC cerrada es una accion administrativa
        // que debe quedar trazada — que usuario reabrio, cuando, sobre que HC
        // y de que paciente. En caso de disputas clinicas o auditoria externa
        // (SOAT, Supersalud) este es el rastro para reconstruir el flujo.
        audit.Write(actor, "historia-clinica.reabrir", nameof(HistoriaClinica), e.Id,
            previousValue: new { estado = HistoriaClinicaEstado.Cerrada.ToString(), fechaCierre = fechaCierrePrev },
            newValue: new { estado = e.Estado.ToString(), pacienteId = e.PacienteId, formDefinitionId = e.FormDefinitionId, especialista = e.EspecialistaNombre },
            tenantId: e.TenantId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DescartarAsync(Guid id, string? motivo, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (e is null) { return false; }
        var estadoPrev = e.Estado;
        e.Estado = HistoriaClinicaEstado.Inactiva;
        e.MotivoInactivacion = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        e.FechaCierre = DateTimeOffset.UtcNow;
        audit.Write(actor, "historia-clinica.descartar", nameof(HistoriaClinica), e.Id,
            previousValue: new { estado = estadoPrev.ToString() },
            newValue: new { estado = e.Estado.ToString(), motivo = e.MotivoInactivacion, fechaCierre = e.FechaCierre, pacienteId = e.PacienteId },
            tenantId: e.TenantId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Guid?> BuscarUltimaAbiertaPorPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        // Si hay varias abiertas (raro pero posible si el profesional cerro sesion
        // sin cerrar la HC), tomamos la mas reciente.
        return await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId && h.Estado == HistoriaClinicaEstado.Abierta)
            .OrderByDescending(h => h.FechaApertura)
            .Select(h => (Guid?)h.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> BuscarAbiertaDelProfesionalAsync(Guid pacienteId, Guid profesionalId, Guid formDefinitionId, CancellationToken ct = default)
    {
        // Reanudar HC en curso: solo cuenta una abierta del mismo profesional
        // sobre el mismo formato. Si hay varias (raro), la mas reciente.
        return await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId
                     && h.ProfesionalId == profesionalId
                     && h.FormDefinitionId == formDefinitionId
                     && h.Estado == HistoriaClinicaEstado.Abierta)
            .OrderByDescending(h => h.FechaApertura)
            .Select(h => (Guid?)h.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<HistoriaClinicaDetailDto?> CopiarAsync(CopiarHistoriaRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        // Cargar la HC origen — sirve tanto para copiar sus campos base como para
        // validar que existe y pertenece al tenant (via Query Filter).
        var source = await db.HistoriasClinicas.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == req.SourceHistoriaId, ct);
        if (source is null) { return null; }
        if (source.Estado == HistoriaClinicaEstado.Inactiva)
        {
            throw new InvalidOperationException("No se puede copiar una historia inactiva.");
        }

        // Crear la copia. Estrategia:
        //   - Estado siempre Abierta y FechaApertura = ahora (es una HC nueva).
        //   - Profesional/Especialista: usar los del actor si vienen; si no, heredar los del origen.
        //   - RIPS: se heredan del origen — el doctor no tiene que re-elegirlos.
        //   - ValoresJson: se copia tal cual del origen. El frontend correra el prefill de
        //     paciente/sistema/firmas DESPUES para que refresque fecha/hora/medico logueado y
        //     "gane" sobre los valores copiados en esas keys (los campos clinicos libres se
        //     preservan porque el prefill no los toca).
        var ahora = DateTimeOffset.UtcNow;
        var nueva = new HistoriaClinica
        {
            TenantId = tid,
            PacienteId = source.PacienteId,
            FormDefinitionId = source.FormDefinitionId,
            ValoresJson = string.IsNullOrWhiteSpace(source.ValoresJson) ? "{}" : source.ValoresJson,
            Estado = HistoriaClinicaEstado.Abierta,
            FechaApertura = ahora,
            EspecialistaNombre = req.EspecialistaNombre ?? source.EspecialistaNombre,
            ProfesionalId = req.ProfesionalId ?? source.ProfesionalId,
            RipsViaIngresoCodigo = source.RipsViaIngresoCodigo,
            RipsViaIngresoNombre = source.RipsViaIngresoNombre,
            RipsFinalidadCodigo = source.RipsFinalidadCodigo,
            RipsFinalidadNombre = source.RipsFinalidadNombre,
            RipsCausaExternaCodigo = source.RipsCausaExternaCodigo,
            RipsCausaExternaNombre = source.RipsCausaExternaNombre
        };
        db.HistoriasClinicas.Add(nueva);
        await db.SaveChangesAsync(ct);

        // Clonar las 7 colecciones clinicas. Cada item nuevo lleva Id fresco + HistoriaClinicaId
        // apuntando a la HC copia. Escalas / Documentos NO se copian: son adjuntos con fecha propia.
        var meds = await db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == source.Id).ToListAsync(ct);
        foreach (var m in meds)
        {
            db.HistoriaClinicaMedicamentos.Add(new HistoriaClinicaMedicamento
            {
                TenantId = tid, HistoriaClinicaId = nueva.Id,
                MedicamentoId = m.MedicamentoId,
                NombreMedicamento = m.NombreMedicamento,
                CodigoMedicamento = m.CodigoMedicamento,
                Cantidad = m.Cantidad, Frecuencia = m.Frecuencia,
                Dias = m.Dias, Posologia = m.Posologia,
                Observacion = m.Observacion, MipresUrl = m.MipresUrl,
                Orden = m.Orden
            });
        }
        var insumos = await db.HistoriaClinicaInsumos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == source.Id).ToListAsync(ct);
        foreach (var i in insumos)
        {
            db.HistoriaClinicaInsumos.Add(new HistoriaClinicaInsumo
            {
                TenantId = tid, HistoriaClinicaId = nueva.Id,
                Codigo = i.Codigo, Descripcion = i.Descripcion,
                Cantidad = i.Cantidad, Observaciones = i.Observaciones,
                MipresUrl = i.MipresUrl, Orden = i.Orden
            });
        }
        var rems = await db.HistoriaClinicaRemisiones.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == source.Id).ToListAsync(ct);
        foreach (var r in rems)
        {
            db.HistoriaClinicaRemisiones.Add(new HistoriaClinicaRemision
            {
                TenantId = tid, HistoriaClinicaId = nueva.Id,
                Capitulo = r.Capitulo,
                EspecialidadCodigo = r.EspecialidadCodigo,
                EspecialidadNombre = r.EspecialidadNombre,
                Cantidad = r.Cantidad, Motivo = r.Motivo, Orden = r.Orden
            });
        }
        var incs = await db.HistoriaClinicaIncapacidades.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == source.Id).ToListAsync(ct);
        foreach (var x in incs)
        {
            db.HistoriaClinicaIncapacidades.Add(new HistoriaClinicaIncapacidad
            {
                TenantId = tid, HistoriaClinicaId = nueva.Id,
                Motivo = x.Motivo,
                FechaDesde = x.FechaDesde, FechaHasta = x.FechaHasta,
                Dias = x.Dias, Tipo = x.Tipo, Orden = x.Orden
            });
        }
        var certs = await db.HistoriaClinicaCertificaciones.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == source.Id).ToListAsync(ct);
        foreach (var c in certs)
        {
            db.HistoriaClinicaCertificaciones.Add(new HistoriaClinicaCertificacion
            {
                TenantId = tid, HistoriaClinicaId = nueva.Id,
                Titulo = c.Titulo, Contenido = c.Contenido, Orden = c.Orden
            });
        }
        var ords = await db.HistoriaClinicaOrdenesServicio.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == source.Id).ToListAsync(ct);
        foreach (var o in ords)
        {
            db.HistoriaClinicaOrdenesServicio.Add(new HistoriaClinicaOrdenServicio
            {
                TenantId = tid, HistoriaClinicaId = nueva.Id,
                ServicioContratoId = o.ServicioContratoId,
                CodigoServicio = o.CodigoServicio,
                Descripcion = o.Descripcion,
                Cantidad = o.Cantidad, Observaciones = o.Observaciones,
                Orden = o.Orden
            });
        }
        var exts = await db.HistoriaClinicaOrdenesExternas.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == source.Id).ToListAsync(ct);
        foreach (var e in exts)
        {
            db.HistoriaClinicaOrdenesExternas.Add(new HistoriaClinicaOrdenExterna
            {
                TenantId = tid, HistoriaClinicaId = nueva.Id,
                Tipo = e.Tipo, Codigo = e.Codigo,
                Descripcion = e.Descripcion,
                Cantidad = e.Cantidad, Observaciones = e.Observaciones,
                Orden = e.Orden
            });
        }

        audit.Write(actor, "historia-clinica.copiar", nameof(HistoriaClinica), nueva.Id,
            previousValue: null,
            newValue: new
            {
                origenId = source.Id,
                formDefinitionId = nueva.FormDefinitionId,
                pacienteId = nueva.PacienteId,
                items = new
                {
                    medicamentos = meds.Count,
                    insumos = insumos.Count,
                    remisiones = rems.Count,
                    incapacidades = incs.Count,
                    certificaciones = certs.Count,
                    ordenesServicio = ords.Count,
                    ordenesExternas = exts.Count
                }
            },
            tenantId: tid);
        await db.SaveChangesAsync(ct);

        return await GetAsync(nueva.Id, ct);
    }
}
