using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Buzon de correo de un tenant del que se leen mensajes para convertirlos en PQR. TENANT-SCOPED.
/// Un tenant puede tener N buzones. La App Password IMAP se guarda cifrada (ISecretProtector) y
/// nunca se expone ni se loggea. Un agente IA clasifica cada correo (es PQR? de que tipo?) y, si
/// aplica, se crea una tarjeta en el tablero destino (normalmente el tablero "PQR").
/// </summary>
public class TenantEmailIngestConfig : TenantEntity
{
    /// <summary>Etiqueta legible del buzon (ej. "PQR FOMAG").</summary>
    public string Nombre { get; set; } = null!;

    public EmailIngestProvider Provider { get; set; } = EmailIngestProvider.Imap;

    /// <summary>Host IMAP. Gmail: imap.gmail.com.</summary>
    public string ImapHost { get; set; } = "imap.gmail.com";

    /// <summary>Puerto IMAP. Gmail SSL: 993.</summary>
    public int ImapPort { get; set; } = 993;

    /// <summary>Usar SSL/TLS implicito (recomendado true para 993).</summary>
    public bool ImapUseSsl { get; set; } = true;

    /// <summary>Direccion de correo (tambien es el usuario IMAP).</summary>
    public string EmailAddress { get; set; } = null!;

    /// <summary>App Password IMAP cifrada en reposo. La contraseña normal de la cuenta NO sirve para IMAP.</summary>
    public string? AppPasswordEncrypted { get; set; }

    /// <summary>Carpeta a leer. Por defecto INBOX.</summary>
    public string Folder { get; set; } = "INBOX";

    /// <summary>Agente IA que clasifica los correos (referencia a AiAgent del tenant).</summary>
    public Guid? ClassifierAgentId { get; set; }

    /// <summary>Tablero destino donde se crean las tarjetas PQR (normalmente el tablero "PQR").</summary>
    public Guid? TargetBoardId { get; set; }

    /// <summary>Columna destino. Si es null, se usa la primera columna del tablero (ej. "Radicado").</summary>
    public Guid? TargetColumnId { get; set; }

    /// <summary>Cada cuantos minutos el poller revisa este buzon.</summary>
    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>Ventana inicial (dias hacia atras) que se lee la primera vez.</summary>
    public int LookbackDays { get; set; } = 7;

    /// <summary>Maximo de correos por corrida (proteccion contra buzones grandes).</summary>
    public int MaxPorCorrida { get; set; } = 25;

    /// <summary>Solo procesar correos no leidos.</summary>
    public bool OnlyUnread { get; set; } = true;

    /// <summary>Marcar como leidos los correos procesados (evita reprocesar el mismo universo).</summary>
    public bool MarkAsRead { get; set; } = true;

    /// <summary>
    /// Etiqueta (Gmail label / carpeta IMAP) que se aplica a cada correo procesado, como marca
    /// VISIBLE de "ya procesado" para el operador humano (mas claro que solo marcarlo leido).
    /// Debe ser una etiqueta EXISTENTE de la cuenta. Null/vacio = no se aplica etiqueta.
    /// </summary>
    public string? ProcessedLabel { get; set; }

    /// <summary>Si esta encendido para el poller automatico.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Modo prueba: para ensayar la clasificacion sin tocar el buzon real ni la operacion.
    /// Cuando esta encendido, "Procesar ahora" lee TODOS los correos de la Carpeta (ignora
    /// OnlyUnread), NO marca leidos ni aplica la etiqueta de procesado (Gmail queda intacto),
    /// SI crea tarjetas (marcadas como prueba) y el poller automatico lo omite. El dedup por
    /// Message-ID sigue activo; el boton "Limpiar corrida de prueba" borra tarjetas + logs de
    /// prueba para poder repetir con los mismos correos.
    /// </summary>
    public bool ModoPrueba { get; set; }

    /// <summary>Ultima vez que el poller reviso este buzon.</summary>
    public DateTimeOffset? LastPolledAt { get; set; }

    /// <summary>Ultimo error (conexion IMAP, IA, etc.). Null si la ultima corrida fue limpia.</summary>
    public string? LastError { get; set; }

    /// <summary>Resumen de la ultima corrida (ej. "3 leidos, 2 PQR, 1 descartado").</summary>
    public string? LastResultSummary { get; set; }
}
