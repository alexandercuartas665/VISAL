using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy;

/// <summary>Paciente marcado como de prueba, con conteo rapido de su actividad.</summary>
public sealed record PacientePruebaDto(
    Guid Id,
    string NombreCompleto,
    string? NumeroDocumento,
    string? TipoDocumento,
    int Historias,
    int Atenciones);

/// <summary>Resultado de una busqueda de pacientes para marcarlos como de prueba.</summary>
public sealed record PacienteBusquedaDto(
    Guid Id,
    string NombreCompleto,
    string? NumeroDocumento,
    string? TipoDocumento,
    bool EsPrueba);

/// <summary>Conteo de lo que se borraria al limpiar un paciente de prueba.</summary>
public sealed record PacientePruebaConteoDto(
    int Historias,
    int Atenciones,
    int Escalas,
    int Documentos,
    int Medicamentos,
    int Notas,
    int Firmas,
    int Encuestas)
{
    public int Total => Historias + Atenciones + Escalas + Documentos + Medicamentos + Notas + Firmas + Encuestas;
}

/// <summary>Resumen de la limpieza ejecutada (filas borradas por grupo).</summary>
public sealed record PacientePruebaLimpiezaResultDto(
    int Historias,
    int Atenciones,
    int Notas,
    int Firmas,
    int Encuestas,
    int OrdenesEmitidas,
    int Otros)
{
    public int Total => Historias + Atenciones + Notas + Firmas + Encuestas + OrdenesEmitidas + Otros;
}

/// <summary>
/// Gestion de "pacientes de prueba": marcar/desmarcar y limpiar toda la actividad
/// clinica/operativa de un paciente conservando el registro del paciente, su
/// afiliacion (contratos) y sus contactos de emergencia. Pensado para dejar los
/// pacientes de demo/pruebas reutilizables sin recrearlos.
/// </summary>
public interface IPacientePruebaService
{
    /// <summary>Pacientes marcados como de prueba (del tenant actual).</summary>
    Task<IReadOnlyList<PacientePruebaDto>> ListarAsync(CancellationToken ct = default);

    /// <summary>Busca pacientes por nombre o documento para marcarlos como de prueba.</summary>
    Task<IReadOnlyList<PacienteBusquedaDto>> BuscarAsync(string termino, CancellationToken ct = default);

    /// <summary>Marca/desmarca un paciente como de prueba.</summary>
    Task<bool> MarcarAsync(Guid pacienteId, bool esPrueba, Guid actor, CancellationToken ct = default);

    /// <summary>Conteo previo de lo que se borraria al limpiar el paciente.</summary>
    Task<PacientePruebaConteoDto> ContarAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>
    /// Borra toda la actividad clinica/operativa del paciente (historias y sus
    /// hijas, atenciones/turnos, notas, ordenes emitidas, firmas, encuestas, chat,
    /// alertas, llamadas, RDA) conservando el paciente, su contrato/afiliacion y
    /// sus contactos de emergencia. Solo procede si el paciente esta marcado como
    /// de prueba. Todo en una sola transaccion.
    /// </summary>
    Task<PacientePruebaLimpiezaResultDto> LimpiarAsync(Guid pacienteId, Guid actor, CancellationToken ct = default);
}

public sealed class PacientePruebaService(IApplicationDbContext db, ITenantContext tenant) : IPacientePruebaService
{
    public async Task<IReadOnlyList<PacientePruebaDto>> ListarAsync(CancellationToken ct = default)
    {
        // Query filter de tenant aplica automaticamente.
        var pacientes = await db.Pacientes.AsNoTracking()
            .Where(p => p.EsPrueba)
            .OrderBy(p => p.NombreCompleto)
            .Select(p => new { p.Id, p.NombreCompleto, p.NumeroDocumento, p.TipoDocumento })
            .ToListAsync(ct);

        var res = new List<PacientePruebaDto>(pacientes.Count);
        foreach (var p in pacientes)
        {
            var hist = await db.HistoriasClinicas.AsNoTracking().CountAsync(h => h.PacienteId == p.Id, ct);
            var aten = await db.Asignaciones.AsNoTracking().CountAsync(a => a.PacienteId == p.Id, ct);
            res.Add(new PacientePruebaDto(p.Id, p.NombreCompleto, p.NumeroDocumento, p.TipoDocumento, hist, aten));
        }
        return res;
    }

    public async Task<IReadOnlyList<PacienteBusquedaDto>> BuscarAsync(string termino, CancellationToken ct = default)
    {
        termino = (termino ?? "").Trim();
        if (termino.Length < 2) { return Array.Empty<PacienteBusquedaDto>(); }
        var t = termino.ToLower();
        return await db.Pacientes.AsNoTracking()
            .Where(p => p.NombreCompleto.ToLower().Contains(t)
                     || (p.NumeroDocumento != null && p.NumeroDocumento.ToLower().Contains(t)))
            .OrderBy(p => p.NombreCompleto)
            .Take(20)
            .Select(p => new PacienteBusquedaDto(p.Id, p.NombreCompleto, p.NumeroDocumento, p.TipoDocumento, p.EsPrueba))
            .ToListAsync(ct);
    }

    public async Task<bool> MarcarAsync(Guid pacienteId, bool esPrueba, Guid actor, CancellationToken ct = default)
    {
        var p = await db.Pacientes.FirstOrDefaultAsync(x => x.Id == pacienteId, ct);
        if (p is null) { return false; }
        p.EsPrueba = esPrueba;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PacientePruebaConteoDto> ContarAsync(Guid pacienteId, CancellationToken ct = default)
    {
        var hcIds = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.PacienteId == pacienteId).Select(h => h.Id).ToListAsync(ct);

        var historias = hcIds.Count;
        var atenciones = await db.Asignaciones.AsNoTracking().CountAsync(a => a.PacienteId == pacienteId, ct);
        var notas = await db.NotasMedicas.AsNoTracking().CountAsync(n => n.PacienteId == pacienteId, ct);
        var firmas = await db.FirmaPacienteRequests.AsNoTracking().CountAsync(f => f.PacienteId == pacienteId, ct);
        var encuestas = await db.SeguimientoEncuestas.AsNoTracking().CountAsync(e => e.PacienteId == pacienteId, ct);
        var escalas = hcIds.Count == 0 ? 0
            : await db.HistoriaClinicaEscalas.AsNoTracking().CountAsync(e => hcIds.Contains(e.HistoriaClinicaId), ct);
        var documentos = hcIds.Count == 0 ? 0
            : await db.HistoriaClinicaDocumentos.AsNoTracking().CountAsync(d => hcIds.Contains(d.HistoriaClinicaId), ct);
        var medicamentos = hcIds.Count == 0 ? 0
            : await db.HistoriaClinicaMedicamentos.AsNoTracking().CountAsync(m => hcIds.Contains(m.HistoriaClinicaId), ct);

        return new PacientePruebaConteoDto(historias, atenciones, escalas, documentos, medicamentos, notas, firmas, encuestas);
    }

    public async Task<PacientePruebaLimpiezaResultDto> LimpiarAsync(Guid pacienteId, Guid actor, CancellationToken ct = default)
    {
        // Guard 1: el paciente debe existir y ser del tenant actual (query filter).
        var pac = await db.Pacientes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pacienteId, ct);
        if (pac is null)
        {
            throw new InvalidOperationException("Paciente no encontrado en este tenant.");
        }
        // Guard 2: solo se limpian pacientes marcados como de prueba (evita borrar
        // por error la actividad de un paciente real).
        if (!pac.EsPrueba)
        {
            throw new InvalidOperationException("El paciente no esta marcado como de prueba. Marcalo primero para poder limpiarlo.");
        }
        if (tenant.TenantId is not Guid)
        {
            throw new InvalidOperationException("Sin tenant activo.");
        }

        var p = pacienteId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1) Tablas RESTRICT que bloquean el borrado de historias, mas la tabla de
        //    ordenes externas (sin FK) y los logs de verificacion (sin FK/DbSet).
        var logs = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM verificacion_orden_logs
            WHERE orden_medicamento_publica_id IN (
              SELECT id FROM ordenes_medicamentos_publicas
              WHERE historia_clinica_id IN (SELECT id FROM historias_clinicas WHERE paciente_id = {p}))", ct);
        var ordenes = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM ordenes_medicamentos_publicas
            WHERE historia_clinica_id IN (SELECT id FROM historias_clinicas WHERE paciente_id = {p})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM revisiones_clinica
            WHERE historia_clinica_id IN (SELECT id FROM historias_clinicas WHERE paciente_id = {p})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM rda_eventos WHERE paciente_id = {p}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM historia_clinica_ordenes_externas
            WHERE historia_clinica_id IN (SELECT id FROM historias_clinicas WHERE paciente_id = {p})", ct);

        // 2) Notas y sus documentos (nota_medica_documentos tiene FK ON DELETE SET
        //    NULL, por eso se borran explicitamente por paciente).
        var notaDocs = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM nota_medica_documentos WHERE paciente_id = {p}", ct);
        var notas = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM notas_medicas WHERE paciente_id = {p}", ct);

        // 3) Historias clinicas: cascadea medicamentos, escalas, documentos,
        //    remisiones, incapacidades, insumos, certificaciones, ordenes_servicio,
        //    suministros y asignacion_turno_sesion_hcs.
        var historias = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM historias_clinicas WHERE paciente_id = {p}", ct);

        // 4) Atenciones/turnos: asignaciones cascadea turnos->sesiones->hcs; lotes
        //    cascadea asignaciones que aun cuelguen de un lote.
        var asignaciones = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM asignaciones WHERE paciente_id = {p}", ct);
        var lotes = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM asignacion_lotes WHERE paciente_id = {p}", ct);

        // 5) Otras tablas operativas ligadas al paciente.
        var firmas = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM firma_paciente_requests WHERE paciente_id = {p}", ct);
        var encuestas = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM seguimiento_encuestas WHERE paciente_id = {p}", ct);
        var chat = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM asistente_chat_mensajes WHERE paciente_id = {p}", ct);
        var alertas = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM alerta_envios WHERE paciente_id = {p}", ct);
        var llamadas = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM llamadas_voz WHERE paciente_id = {p}", ct);

        await tx.CommitAsync(ct);

        var otros = logs + notaDocs + chat + alertas + llamadas;
        return new PacientePruebaLimpiezaResultDto(
            Historias: historias,
            Atenciones: asignaciones + lotes,
            Notas: notas,
            Firmas: firmas,
            Encuestas: encuestas,
            OrdenesEmitidas: ordenes,
            Otros: otros);
    }
}
