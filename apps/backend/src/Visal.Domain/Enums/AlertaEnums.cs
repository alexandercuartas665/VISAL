namespace Visal.Domain.Enums;

/// <summary>Condicion clinica que dispara una alerta sobre una asignacion.</summary>
public enum AlertaCondicion
{
    /// <summary>El paciente tiene al menos una sesion programada sin atender (Programado/Atendido/EnProgreso).</summary>
    SesionPendiente = 1,
    /// <summary>Todas las atenciones esperadas del servicio quedaron cerradas (Terminado).</summary>
    AtencionesTerminadas = 2,
}

/// <summary>Como se decide en que dia dispara la regla.</summary>
public enum AlertaDisparoTipo
{
    /// <summary>Uno o varios dias fijos del mes (ej. 15,16,17). Una vez por mes por asignacion.</summary>
    DiasDelMes = 1,
    /// <summary>N meses despues de una fecha ancla (ver <see cref="AlertaAnclaRelativa"/>).</summary>
    MesesDespues = 2,
}

/// <summary>Fecha desde la que se cuentan los "N meses despues".</summary>
public enum AlertaAnclaRelativa
{
    /// <summary>Fecha de la ultima sesion atendida (cerrada) del paciente/servicio.</summary>
    UltimaAtencion = 1,
    /// <summary>Fecha final (FechaFinal) de la asignacion.</summary>
    FinAsignacion = 2,
}

/// <summary>A quien se dirige la alerta.</summary>
public enum AlertaDestinatario
{
    /// <summary>El profesional que atendio (celular; correo solo si tiene usuario del sistema).</summary>
    DoctorAtendio = 1,
    /// <summary>Un usuario del sistema fijo (correo; WhatsApp solo si tiene profesional vinculado con celular).</summary>
    UsuarioSistema = 2,
    /// <summary>El propio paciente (celular/correo de su ficha).</summary>
    Paciente = 3,
}

/// <summary>Canal por el que se envia la alerta.</summary>
public enum AlertaCanal
{
    Correo = 1,
    WhatsApp = 2,
}

/// <summary>Estado de gestion de una alerta emitida (tarjeta en la bandeja del modulo Alertas).</summary>
public enum AlertaGestion
{
    /// <summary>Recien disparada, sin gestionar.</summary>
    Nueva = 1,
    /// <summary>Ya la reviso/atendio un usuario.</summary>
    Atendida = 2,
    /// <summary>Descartada (no requiere accion).</summary>
    Descartada = 3,
}
