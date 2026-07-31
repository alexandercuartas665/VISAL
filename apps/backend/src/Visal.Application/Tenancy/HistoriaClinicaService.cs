using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision;
using Visal.Application.Revision.Ia;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class HistoriaClinicaService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IAuditWriter audit,
    IRevisionPolicyService revPolicy,
    IRevisionKanbanService kanban,
    IPreRevisionIaQueue preRevisionQueue,
    IPreRevisionIaPendingStore preRevisionStore,
    IAtencionOrdenService atencionOrden) : IHistoriaClinicaService
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
            // EF Core traduce esto a SQL — el ctor debe llamarse con TODOS los
            // args (los expression trees no permiten opcionales), asi que
            // pasamos SesionNumero=null aqui y lo enriquecemos abajo con un
            // JOIN al pivote AsignacionTurnoSesionHc.
            .Select(x => new HistoriaClinicaResumenDto(
                x.h.Id, x.f.Id, x.f.Codigo, x.f.Nombre,
                x.h.Estado.ToString(), x.h.FechaApertura, x.h.FechaCierre,
                x.h.EspecialistaNombre, x.h.MotivoInactivacion, x.h.ProfesionalId,
                (int?)null))
            .ToListAsync(ct);

        // Enriquecer con SesionNumero (nGlobal cronologico) via el pivote
        // AsignacionTurnoSesionHc -> AsignacionTurnoSesion -> AsignacionTurno.
        // Regla 1 sesion <-> 1 HC (ver CrearAsync) hace que a lo sumo haya un
        // resultado por HC. NOTA: no leemos s.SessionNo (siempre 1 desde
        // Cantidad=1 por turno, task #147); calculamos la posicion del turno
        // dentro de su asignacion ordenando por CreatedAt asc (base 1) para
        // que el badge coincida con la parrilla /atencion y con /ordenes.
        if (rows.Count > 0)
        {
            var hcIds = rows.Select(r => r.Id).ToList();
            var pivotes = await db.AsignacionTurnoSesionHcs.AsNoTracking()
                .Where(p => hcIds.Contains(p.HistoriaClinicaId))
                .Join(db.AsignacionTurnoSesiones.AsNoTracking(),
                      p => p.SesionId, s => s.Id,
                      (p, s) => new { p.HistoriaClinicaId, s.AsignacionTurnoId })
                .ToListAsync(ct);
            var hcToTurno = pivotes
                .GroupBy(x => x.HistoriaClinicaId)
                .ToDictionary(g => g.Key, g => g.First().AsignacionTurnoId);
            var turnoIds = hcToTurno.Values.Distinct().ToList();
            var turnoToAsig = turnoIds.Count == 0
                ? new Dictionary<Guid, Guid>()
                : await db.AsignacionTurnos.AsNoTracking()
                    .Where(t => turnoIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.AsignacionId })
                    .ToDictionaryAsync(x => x.Id, x => x.AsignacionId, ct);
            var asigIds = turnoToAsig.Values.Distinct().ToList();
            var turnoOrden = new Dictionary<Guid, int>();
            if (asigIds.Count > 0)
            {
                var todosTurnos = await db.AsignacionTurnos.AsNoTracking()
                    .Where(t => asigIds.Contains(t.AsignacionId))
                    .Select(t => new { t.Id, t.AsignacionId, t.CreatedAt })
                    .ToListAsync(ct);
                foreach (var grp in todosTurnos.GroupBy(x => x.AsignacionId))
                {
                    // Tiebreaker por Id cuando CreatedAt colisiona (seeds masivos):
                    // asegura que el badge muestre el mismo nGlobal que la grilla
                    // /atencion y que el validador de orden secuencial.
                    var lista = grp.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList();
                    for (int i = 0; i < lista.Count; i++)
                    {
                        turnoOrden[lista[i].Id] = i + 1;
                    }
                }
            }
            rows = rows
                .Select(r => hcToTurno.TryGetValue(r.Id, out var turnoIdHc)
                             && turnoOrden.TryGetValue(turnoIdHc, out var nGlobal)
                    ? r with { SesionNumero = nGlobal }
                    : r)
                .ToList();
        }

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

        // RIPS Via de ingreso: se movio al modulo /asignacion (se captura una sola
        // vez por servicio contratado). Si la HC nace desde un turno, la Via viene
        // de la Asignacion como snapshot inmutable — la que envie el request se
        // ignora. Cuando no hay turno (HC libre desde admision), seguimos aceptando
        // la Via del request para no romper ese flujo.
        string? viaCodigoResuelto = req.RipsViaIngresoCodigo;
        string? viaNombreResuelto = req.RipsViaIngresoNombre;
        if (req.AsignacionTurnoId is Guid turnoId0)
        {
            var viaAsig = await (
                from t in db.AsignacionTurnos.AsNoTracking()
                join a in db.Asignaciones.AsNoTracking() on t.AsignacionId equals a.Id
                where t.Id == turnoId0
                select new { a.RipsViaIngresoCodigo, a.RipsViaIngresoNombre })
                .FirstOrDefaultAsync(ct);
            if (viaAsig is not null
                && !string.IsNullOrWhiteSpace(viaAsig.RipsViaIngresoCodigo))
            {
                viaCodigoResuelto = viaAsig.RipsViaIngresoCodigo;
                viaNombreResuelto = viaAsig.RipsViaIngresoNombre;
            }
        }

        // Validacion RIPS: Finalidad + Causa siempre; Via debe venir de la Asignacion
        // (o del request en flujos legacy).
        if (string.IsNullOrWhiteSpace(viaCodigoResuelto)
            || string.IsNullOrWhiteSpace(req.RipsFinalidadCodigo)
            || string.IsNullOrWhiteSpace(req.RipsCausaExternaCodigo))
        {
            throw new InvalidOperationException(
                "Debes indicar Via de ingreso (en la Asignacion), Finalidad de la consulta y Causa externa (datos RIPS obligatorios).");
        }

        // Resolvemos el Id de la sesion (AsignacionTurnoSesion). La UI /atencion
        // envia (AsignacionTurnoId, SessionNo) — la fila puede no existir aun
        // porque solo se crea al cerrar la sesion. La creamos aqui como parte
        // del flujo de apertura de HC para tener un ancla estable en el pivote.
        Guid? sesionResuelta = req.SesionId;
        if (sesionResuelta is null
            && req.AsignacionTurnoId is Guid atId
            && req.SessionNo is int sno && sno >= 1)
        {
            var existente = await db.AsignacionTurnoSesiones
                .FirstOrDefaultAsync(s => s.AsignacionTurnoId == atId && s.SessionNo == sno, ct);
            if (existente is not null)
            {
                sesionResuelta = existente.Id;
            }
            else
            {
                var nueva = new AsignacionTurnoSesion
                {
                    TenantId = tid,
                    AsignacionTurnoId = atId,
                    SessionNo = sno,
                    Completado = false
                };
                db.AsignacionTurnoSesiones.Add(nueva);
                await db.SaveChangesAsync(ct);
                sesionResuelta = nueva.Id;
            }
        }

        // Gate de orden secuencial /atencion: cuando la HC viene desde una sesion
        // programada, la UI ya bloqueo el boton pero validamos de nuevo aqui como
        // defensa contra clientes que evadan la UI (Blazor Server + eventos JS
        // manipulados, o llamadas via consola). Si el usuario tiene permiso
        // "atencion.saltar-orden" o es Owner/Admin, el servicio devuelve null y
        // pasamos libre.
        if (sesionResuelta is Guid sesionValidar)
        {
            var bloqueo = await atencionOrden.ValidarAperturaAsync(sesionValidar, actor, ct);
            if (bloqueo is not null)
            {
                throw new InvalidOperationException(bloqueo.Motivo);
            }

            // Regla 1 sesion <-> 1 HC: bloqueamos crear una segunda HC para la
            // misma sesion. La tabla pivote AsignacionTurnoSesionHc es M:N por
            // diseño (ver entidad) pero el modulo /atencion requiere unicidad
            // aqui — si ya existe alguna HC vinculada, el profesional debe
            // consultar el historial en vez de crear otra.
            var yaTieneHc = await db.AsignacionTurnoSesionHcs
                .AnyAsync(p => p.SesionId == sesionValidar, ct);
            if (yaTieneHc)
            {
                throw new InvalidOperationException(
                    "Ya existe una historia clinica para esta sesion. Consulta el historial del paciente para verla.");
            }
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
            RipsViaIngresoCodigo = viaCodigoResuelto,
            RipsViaIngresoNombre = viaNombreResuelto,
            RipsFinalidadCodigo = req.RipsFinalidadCodigo,
            RipsFinalidadNombre = req.RipsFinalidadNombre,
            RipsCausaExternaCodigo = req.RipsCausaExternaCodigo,
            RipsCausaExternaNombre = req.RipsCausaExternaNombre
        };
        db.HistoriasClinicas.Add(entity);
        await db.SaveChangesAsync(ct);

        // Vinculo HC <-> Sesion cuando la HC nace desde /atencion. Sin este pivote
        // el modulo no puede marcar Completado ni bloquear el orden secuencial.
        // La sesion queda Pendiente (Completado=false) porque la HC arranca Abierta;
        // se marcara Completada cuando se llame CerrarAsync.
        if (sesionResuelta is Guid sesionId)
        {
            await VincularSesionYRecalcularAsync(entity.Id, sesionId, tid, ct);
        }

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
        // Safety net: rellenar defaults ausentes segun el schema. Blinda contra
        // clientes viejos o flows que no pasen por el FormViewer nuevo con
        // hidratacion al abrir. Idempotente y sin sobrescribir vaciados
        // deliberados del doctor. Ver DefaultValuesHelper.HidratarDefaultsAusentes.
        e.ValoresJson = await EnriquecerConDefaultsAsync(e.FormDefinitionId, valoresJson, ct);
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
        // Safety net al Cerrar: si el cliente mando JSON nuevo lo enriquecemos
        // con defaults; si no mando nada (Cerrar sin cambios), tambien
        // enriquecemos el JSON ya persistido — asi HCs viejas se blindan al
        // menos en el momento del cierre.
        var jsonBase = string.IsNullOrWhiteSpace(valoresJson) ? e.ValoresJson : valoresJson;
        e.ValoresJson = await EnriquecerConDefaultsAsync(e.FormDefinitionId, jsonBase, ct);
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
        // Persistir el nuevo estado ANTES del recalculo — el recalculo consulta
        // db.HistoriasClinicas para decidir si la sesion pasa a Completada; si
        // guardamos despues, la query lee el estado viejo (Abierta) y no marca
        // la sesion. Mismo patron en Reabrir/Descartar/Activar mas abajo.
        await db.SaveChangesAsync(ct);
        await RecalcularCompletadoDeSesionesVinculadasAsync(e.Id, ct);

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
        // La HC volvio a Abierta: si era la unica Cerrada en las sesiones vinculadas,
        // esas sesiones vuelven a Pendiente. Save antes del recalc (ver CerrarAsync).
        await db.SaveChangesAsync(ct);
        await RecalcularCompletadoDeSesionesVinculadasAsync(e.Id, ct);
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
        // HC descartada: si era la unica Cerrada de sus sesiones vinculadas,
        // las sesiones vuelven a Pendiente. Save antes del recalc (ver CerrarAsync).
        await db.SaveChangesAsync(ct);
        await RecalcularCompletadoDeSesionesVinculadasAsync(e.Id, ct);
        return true;
    }

    public async Task<bool> ActivarAsync(Guid id, string? motivo, Guid actor, CancellationToken ct = default)
    {
        var e = await db.HistoriasClinicas.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (e is null) { return false; }
        if (e.Estado != HistoriaClinicaEstado.Inactiva)
        {
            throw new InvalidOperationException("Solo se puede activar una historia que este Inactiva.");
        }
        var motivoPrev = e.MotivoInactivacion;
        var fechaCierrePrev = e.FechaCierre;
        e.Estado = HistoriaClinicaEstado.Abierta;
        e.FechaCierre = null;
        e.MotivoInactivacion = null;
        // Auditoria administrativa: reactivar una HC descartada es tan sensible
        // como reabrir una Cerrada — deja rastro de quien lo hizo, motivo del
        // reingreso al flujo y el motivo original del descarte.
        audit.Write(actor, "historia-clinica.activar", nameof(HistoriaClinica), e.Id,
            previousValue: new { estado = HistoriaClinicaEstado.Inactiva.ToString(), motivoInactivacion = motivoPrev, fechaCierre = fechaCierrePrev },
            newValue: new { estado = e.Estado.ToString(), motivoActivacion = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim(), pacienteId = e.PacienteId },
            tenantId: e.TenantId);
        // La HC volvio al flujo (Abierta) desde Inactiva: si estaba sirviendo como
        // "Completado=true" por alguna razon en el pivote, ahora ya no cuenta.
        // Save antes del recalc (ver CerrarAsync).
        await db.SaveChangesAsync(ct);
        await RecalcularCompletadoDeSesionesVinculadasAsync(e.Id, ct);
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

        // Enriquecer los valores copiados con defaults ausentes por si el schema
        // evoluciono despues de que se guardo la HC origen. Idempotente.
        nueva.ValoresJson = await EnriquecerConDefaultsAsync(nueva.FormDefinitionId, nueva.ValoresJson, ct);

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

    /// <summary>
    /// Inserta el pivote AsignacionTurnoSesionHc para vincular la HC recien
    /// creada con la sesion desde donde se abrio en /atencion. Si el pivote
    /// ya existiera (race o retry) el indice unico (sesion_id, hc_id) haria
    /// fallar el insert; por eso chequeamos primero. Luego recalcula el
    /// flag Completado de la sesion (por definicion queda false, la HC nace
    /// Abierta).
    /// </summary>
    private async Task VincularSesionYRecalcularAsync(Guid historiaClinicaId, Guid sesionId, Guid tenantId, CancellationToken ct)
    {
        var yaExiste = await db.AsignacionTurnoSesionHcs
            .AnyAsync(p => p.SesionId == sesionId && p.HistoriaClinicaId == historiaClinicaId, ct);
        if (!yaExiste)
        {
            db.AsignacionTurnoSesionHcs.Add(new AsignacionTurnoSesionHc
            {
                TenantId = tenantId,
                SesionId = sesionId,
                HistoriaClinicaId = historiaClinicaId
            });
            await db.SaveChangesAsync(ct);
        }
        await RecalcularCompletadoDeSesionAsync(sesionId, ct);
    }

    /// <summary>
    /// Recalcula el flag Completado de todas las sesiones vinculadas a
    /// <paramref name="historiaClinicaId"/> via el pivote. Se llama despues de
    /// cambiar el estado de la HC (Cerrar/Reabrir/Descartar/Activar) para que
    /// la parrilla /atencion refleje si la sesion sigue Pendiente o pasa a
    /// Completada.
    ///
    /// Definicion: sesion.Completado = true si al menos UNA HC vinculada esta
    /// en estado Cerrada. HC Abierta o Inactiva NO cuentan como completada.
    /// </summary>
    private async Task RecalcularCompletadoDeSesionesVinculadasAsync(Guid historiaClinicaId, CancellationToken ct)
    {
        var sesionIds = await db.AsignacionTurnoSesionHcs
            .Where(p => p.HistoriaClinicaId == historiaClinicaId)
            .Select(p => p.SesionId)
            .ToListAsync(ct);
        foreach (var sesionId in sesionIds)
        {
            await RecalcularCompletadoDeSesionAsync(sesionId, ct);
        }
    }

    private async Task RecalcularCompletadoDeSesionAsync(Guid sesionId, CancellationToken ct)
    {
        var sesion = await db.AsignacionTurnoSesiones.FirstOrDefaultAsync(s => s.Id == sesionId, ct);
        if (sesion is null) { return; }
        var deberiaEstarCompletada = await db.AsignacionTurnoSesionHcs
            .Where(p => p.SesionId == sesionId)
            .Join(db.HistoriasClinicas, p => p.HistoriaClinicaId, h => h.Id, (_, h) => h.Estado)
            .AnyAsync(estado => estado == HistoriaClinicaEstado.Cerrada, ct);
        if (sesion.Completado != deberiaEstarCompletada)
        {
            sesion.Completado = deberiaEstarCompletada;
            // Al pasar de Pendiente a Completada, si la sesion nunca fue
            // registrada via RegistrarSesionAsync (viene default(DateOnly)),
            // fijamos la fecha con el momento del cierre — asi la parrilla
            // /atencion muestra la fecha real del cierre en vez de 0001-01-01.
            // No sobreescribimos cuando ya venia con fecha (registro manual
            // previo del profesional o import).
            if (deberiaEstarCompletada && sesion.FechaAtencion == default)
            {
                sesion.FechaAtencion = DateOnly.FromDateTime(DateTime.Today);
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Deserializa <paramref name="valoresJson"/>, aplica defaults ausentes
    /// segun el schema del FormDefinition, y devuelve el JSON re-serializado.
    /// Si el schema no se puede cargar o no aplica nada nuevo, devuelve el
    /// JSON original saneado ({} para blanco). Los tenants sin schema (imposible
    /// en la practica pero defensivo) obtienen el JSON tal cual.
    /// </summary>
    private async Task<string> EnriquecerConDefaultsAsync(Guid formDefinitionId, string? valoresJson, CancellationToken ct)
    {
        var jsonSaneado = string.IsNullOrWhiteSpace(valoresJson) ? "{}" : valoresJson;

        var schemaJson = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == formDefinitionId)
            .Select(f => f.SchemaJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(schemaJson)) { return jsonSaneado; }

        Dictionary<string, string?>? valores;
        try
        {
            valores = JsonSerializer.Deserialize<Dictionary<string, string?>>(jsonSaneado)
                      ?? new Dictionary<string, string?>();
        }
        catch
        {
            // JSON malformado: no arriesgamos data, lo dejamos como llego.
            return jsonSaneado;
        }

        var schema = FormSchema.FromJson(schemaJson);
        var cambio = DefaultValuesHelper.HidratarDefaultsAusentes(valores, schema);
        if (!cambio) { return jsonSaneado; }

        return JsonSerializer.Serialize(valores);
    }
}
