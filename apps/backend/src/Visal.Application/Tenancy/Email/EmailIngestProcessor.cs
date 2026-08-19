using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.Email;

public sealed class EmailIngestProcessor : IEmailIngestProcessor
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secret;
    private readonly IImapEmailReader _reader;
    private readonly IAiInferenceService _ai;
    private readonly IUploadStorage _uploads;
    private readonly TimeProvider _clock;

    private const int MaxBodyParaIa = 8000;
    private const int MaxBodyParaTarjeta = 6000;

    /// <summary>
    /// Instrucciones de clasificacion que el sistema entrega al agente elegido (reutilizamos un
    /// agente existente del tenant: aporta proveedor/modelo/credenciales; las instrucciones de la
    /// tarea las pone el sistema para garantizar una salida JSON consistente).
    /// </summary>
    private const string PromptClasificador =
        "Eres un clasificador de PQRS-F (Peticiones, Quejas, Reclamos, Sugerencias, Felicitaciones) de una IPS " +
        "de salud domiciliaria en Colombia. Recibes un correo electronico (remitente, asunto y cuerpo). Determina " +
        "si el correo es una PQRS-F de un usuario/paciente y, si lo es, extrae sus datos.\n\n" +
        "Responde UNICAMENTE con un objeto JSON valido, sin texto adicional y sin bloques de codigo, con esta forma:\n" +
        "{\n" +
        "  \"es_pqr\": true | false,\n" +
        "  \"tipo\": \"Peticion\" | \"Queja\" | \"Reclamo\" | \"Sugerencia\" | \"Felicitacion\",\n" +
        "  \"servicio\": \"servicio o area al que se refiere, vacio si no se menciona\",\n" +
        "  \"descripcion\": \"resumen breve y claro del caso en 1-3 frases\",\n" +
        "  \"nombres\": \"nombre completo del usuario que radica, vacio si no se puede inferir\",\n" +
        "  \"identificacion\": \"numero de documento si aparece, vacio si no\",\n" +
        "  \"celular\": \"telefono de contacto si aparece, vacio si no\",\n" +
        "  \"email\": \"correo del remitente\",\n" +
        "  \"atributo_calidad\": \"atributo de calidad afectado (accesibilidad, oportunidad, seguridad, pertinencia, continuidad), vacio si no aplica\"\n" +
        "}\n\n" +
        "Reglas: si el correo NO es una PQRS-F (spam, notificaciones automaticas, publicidad, boletines, correos " +
        "internos), responde {\"es_pqr\": false}. No inventes datos: si un campo no esta en el correo, dejalo como " +
        "cadena vacia. \"tipo\" debe ser uno de los valores listados. Responde solo el JSON.";

    private static readonly string[] MesesEs =
    {
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    };

    public EmailIngestProcessor(IApplicationDbContext db, ISecretProtector secret, IImapEmailReader reader,
        IAiInferenceService ai, IUploadStorage uploads, TimeProvider clock)
    {
        _db = db;
        _secret = secret;
        _reader = reader;
        _ai = ai;
        _uploads = uploads;
        _clock = clock;
    }

    public async Task<EmailIngestRunResult> ProcessConfigAsync(Guid configId, IProgress<EmailIngestProgress>? progress = null, CancellationToken ct = default)
    {
        var cfg = await _db.TenantEmailIngestConfigs.FirstOrDefaultAsync(c => c.Id == configId, ct);
        if (cfg is null) { return new EmailIngestRunResult(false, 0, 0, 0, 0, 0, "El buzon no existe."); }

        var now = _clock.GetUtcNow();

        // Validaciones de configuracion (sin agente/tablero/clave no hay nada que hacer).
        string? faltante = null;
        if (cfg.ClassifierAgentId is null) { faltante = "Falta el agente clasificador."; }
        else if (cfg.TargetBoardId is null) { faltante = "Falta el tablero destino."; }
        else if (string.IsNullOrEmpty(cfg.AppPasswordEncrypted)) { faltante = "Falta la App Password del buzon."; }
        if (faltante is not null)
        {
            cfg.LastPolledAt = now;
            cfg.LastError = faltante;
            cfg.LastResultSummary = faltante;
            await _db.SaveChangesAsync(ct);
            return new EmailIngestRunResult(false, 0, 0, 0, 0, 0, faltante);
        }

        string password;
        try { password = _secret.Unprotect(cfg.AppPasswordEncrypted!); }
        catch
        {
            const string msg = "La App Password esta cifrada con una version anterior. Vuelve a guardarla.";
            cfg.LastPolledAt = now;
            cfg.LastError = msg;
            cfg.LastResultSummary = msg;
            await _db.SaveChangesAsync(ct);
            return new EmailIngestRunResult(false, 0, 0, 0, 0, 0, msg);
        }

        // En modo prueba se lee siempre desde la ventana de LookbackDays (no desde LastPolledAt) para
        // que la carpeta de prueba se pueda re-leer completa, y se ignora OnlyUnread (los correos de la
        // etiqueta suelen estar leidos). En operacion normal se respeta el watermark y OnlyUnread.
        var since = cfg.ModoPrueba
            ? now.AddDays(-Math.Max(1, cfg.LookbackDays))
            : cfg.LastPolledAt ?? now.AddDays(-Math.Max(1, cfg.LookbackDays));
        var soloNoLeidos = !cfg.ModoPrueba && cfg.OnlyUnread;
        var pars = new ImapConnectionParams(cfg.ImapHost, cfg.ImapPort, cfg.ImapUseSsl, cfg.EmailAddress, password,
            string.IsNullOrWhiteSpace(cfg.Folder) ? "INBOX" : cfg.Folder, soloNoLeidos, Math.Clamp(cfg.MaxPorCorrida, 1, 200), since);

        IReadOnlyList<IncomingEmail> emails;
        try
        {
            emails = await _reader.FetchAsync(pars, ct);
        }
        catch (Exception ex)
        {
            var msg = $"No se pudo leer el buzon: {ex.Message}";
            cfg.LastPolledAt = now;
            cfg.LastError = msg;
            cfg.LastResultSummary = msg;
            await _db.SaveChangesAsync(ct);
            return new EmailIngestRunResult(false, 0, 0, 0, 0, 0, msg);
        }

        // El prompt fuerte de clasificacion (contrato JSON) lo pone el sistema y NO se toca. Si el
        // agente tiene un system_prompt propio, se ANEXA como refuerzo/afinacion del tenant (Opcion B):
        // agrega contexto (servicios de la agencia, matices de clasificacion) sin anular las reglas
        // base, y se recuerda al final que la salida debe seguir siendo el JSON descrito.
        var extraPrompt = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == cfg.ClassifierAgentId!.Value)
            .Select(a => a.SystemPrompt)
            .FirstOrDefaultAsync(ct);
        var promptClasificador = string.IsNullOrWhiteSpace(extraPrompt)
            ? PromptClasificador
            : PromptClasificador
              + "\n\n--- Instrucciones adicionales de la agencia (contexto y afinacion) ---\n"
              + extraPrompt.Trim()
              + "\n\nImportante: independientemente de lo anterior, responde UNICAMENTE con el objeto JSON descrito arriba, sin texto adicional.";

        int creados = 0, descartados = 0, errores = 0, duplicados = 0, adjuntosTotal = 0, idx = 0;
        long tokensTotal = 0;
        var uidsProcesados = new List<long>();
        progress?.Report(new EmailIngestProgress(0, emails.Count, null));

        foreach (var email in emails)
        {
            ct.ThrowIfCancellationRequested();
            idx++;
            // Avance en vivo para la UI: "correo idx de N" + el asunto que se esta procesando ahora.
            progress?.Report(new EmailIngestProgress(idx, emails.Count, email.Subject));
            var messageId = string.IsNullOrWhiteSpace(email.MessageId) ? $"uid:{email.Uid}" : email.MessageId;

            var yaProcesado = await _db.EmailIngestLogs.AnyAsync(l => l.ConfigId == cfg.Id && l.MessageId == messageId, ct);
            if (yaProcesado) { duplicados++; uidsProcesados.Add(email.Uid); continue; }

            var log = new EmailIngestLog
            {
                ConfigId = cfg.Id,
                MessageId = messageId,
                ImapUid = email.Uid,
                FromAddress = email.FromAddress,
                FromName = email.FromName,
                Subject = email.Subject,
                // Npgsql exige offset UTC para 'timestamp with time zone'; el correo trae su propio offset.
                ReceivedAt = email.ReceivedAt == default ? (DateTimeOffset?)null : email.ReceivedAt.ToUniversalTime(),
                EsPrueba = cfg.ModoPrueba,
                ProcessedAt = now
            };

            try
            {
                var promptUsuario = BuildEmailPrompt(email);
                var turns = new List<AiChatTurn> { new("user", promptUsuario) };
                var ia = await _ai.RunAgentAsync(cfg.ClassifierAgentId!.Value, turns, promptClasificador, "email-pqr", ct);
                // Contador de tokens por correo (auditoria de costo por mensaje).
                log.InputTokens = ia.InputTokens;
                log.OutputTokens = ia.OutputTokens;
                tokensTotal += ia.InputTokens + ia.OutputTokens;

                if (!ia.Ok || string.IsNullOrWhiteSpace(ia.Text))
                {
                    log.Resultado = EmailIngestResultTipo.Error;
                    log.ErrorMessage = ia.Error ?? "El agente no devolvio respuesta.";
                    errores++;
                }
                else
                {
                    log.ClassificationJson = ia.Text;
                    var clasif = ParseClassification(ia.Text!);
                    if (clasif is null)
                    {
                        log.Resultado = EmailIngestResultTipo.Error;
                        log.ErrorMessage = "No se pudo interpretar la respuesta del agente como JSON.";
                        errores++;
                    }
                    else if (!clasif.EsPqr)
                    {
                        log.Resultado = EmailIngestResultTipo.NoEsPqr;
                        log.TipoPqrs = clasif.TipoPqrs;
                        descartados++;
                    }
                    else
                    {
                        var (cardId, adjuntos) = await CrearTarjetaPqrAsync(cfg, email, clasif, now, ct);
                        log.Resultado = EmailIngestResultTipo.CreadaPqr;
                        log.TipoPqrs = clasif.TipoPqrs;
                        log.TaskCardId = cardId;
                        log.AttachmentCount = adjuntos;
                        adjuntosTotal += adjuntos;
                        creados++;
                    }
                }
            }
            catch (Exception ex)
            {
                log.Resultado = EmailIngestResultTipo.Error;
                log.ErrorMessage = ex.Message;
                errores++;
            }

            _db.EmailIngestLogs.Add(log);
            await _db.SaveChangesAsync(ct);
            uidsProcesados.Add(email.Uid);
        }

        // Modo prueba: el buzon de Gmail NO se toca (ni marcar leido ni etiquetar), para poder
        // re-ensayar con los mismos correos. En operacion normal si aplica ambas marcas.

        // Marcar leidos (para que la proxima corrida con OnlyUnread no los vuelva a traer).
        if (!cfg.ModoPrueba && cfg.MarkAsRead && uidsProcesados.Count > 0)
        {
            try { await _reader.MarkSeenAsync(pars, uidsProcesados, ct); }
            catch { /* no critico: el dedup por Message-ID ya evita reprocesar */ }
        }

        // Etiquetar como "procesado" (marca VISIBLE para el operador humano). No critico: si el
        // servidor no soporta etiquetas o falla, el dedup por Message-ID igual evita reprocesar.
        string? etiquetaError = null;
        if (!cfg.ModoPrueba && !string.IsNullOrWhiteSpace(cfg.ProcessedLabel) && uidsProcesados.Count > 0)
        {
            try { await _reader.AddLabelAsync(pars, uidsProcesados, cfg.ProcessedLabel!.Trim(), ct); }
            catch (Exception ex) { etiquetaError = $"No se pudo aplicar la etiqueta '{cfg.ProcessedLabel}': {ex.Message}"; }
        }

        // En modo prueba no se avanza el watermark (LastPolledAt) para no contaminar la operacion
        // real: al apagar el modo, produccion arranca desde su ventana normal.
        if (!cfg.ModoPrueba) { cfg.LastPolledAt = now; }
        cfg.LastError = errores > 0 ? $"{errores} correo(s) con error en la ultima corrida." : etiquetaError;
        var prefijo = cfg.ModoPrueba ? "[PRUEBA] " : "";
        cfg.LastResultSummary = $"{prefijo}{emails.Count} leidos · {creados} PQR · {descartados} descartados · {duplicados} ya vistos · {errores} errores · {adjuntosTotal} adjuntos · {tokensTotal} tokens";
        await _db.SaveChangesAsync(ct);

        return new EmailIngestRunResult(true, emails.Count, creados, descartados, errores, duplicados,
            etiquetaError, adjuntosTotal, tokensTotal);
    }

    private static string BuildEmailPrompt(IncomingEmail email)
    {
        var body = email.BodyText ?? "";
        if (body.Length > MaxBodyParaIa) { body = body[..MaxBodyParaIa]; }
        var sb = new StringBuilder();
        sb.AppendLine($"De: {email.FromName} <{email.FromAddress}>");
        sb.AppendLine($"Asunto: {email.Subject}");
        sb.AppendLine($"Fecha: {email.ReceivedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("Cuerpo del correo:");
        sb.AppendLine(body);
        return sb.ToString();
    }

    private async Task<(Guid CardId, int Adjuntos)> CrearTarjetaPqrAsync(TenantEmailIngestConfig cfg, IncomingEmail email,
        EmailPqrClassification clasif, DateTimeOffset now, CancellationToken ct)
    {
        var boardId = cfg.TargetBoardId!.Value;

        // Columna destino: la configurada, o la primera del tablero (ej. "Radicado").
        var columnId = cfg.TargetColumnId;
        if (columnId is null || !await _db.TaskBoardColumns.AnyAsync(c => c.Id == columnId && c.BoardId == boardId, ct))
        {
            columnId = await _db.TaskBoardColumns.Where(c => c.BoardId == boardId)
                .OrderBy(c => c.SortOrder).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        }
        if (columnId is null) { throw new InvalidOperationException("El tablero destino no tiene columnas."); }

        var consecutivo = await SiguienteConsecutivoAsync(boardId, ct);
        var recibido = email.ReceivedAt == default ? now : email.ReceivedAt;

        var fields = new Dictionary<string, string?>
        {
            ["n_consecutivo"] = consecutivo.ToString(CultureInfo.InvariantCulture),
            ["mes"] = MesesEs[recibido.Month - 1],
            ["fecha_radicacion"] = recibido.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["fecha_diligenciamiento"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["nombres_usuario"] = clasif.NombresUsuario,
            ["identificacion"] = clasif.Identificacion,
            ["celular"] = clasif.Celular,
            ["email"] = string.IsNullOrWhiteSpace(clasif.Email) ? email.FromAddress : clasif.Email,
            ["servicio_pqrs"] = clasif.ServicioPqrs,
            ["tipo_pqrs_f"] = clasif.TipoPqrs,
            ["descripcion_pqrsf"] = clasif.DescripcionPqrsf,
            ["atributo_calidad"] = clasif.AtributoCalidad,
            ["medio_llegada"] = "Correo electronico",
            ["estado_solicitud"] = "Radicado"
        };
        // No guardamos claves vacias para no ensuciar el jsonb.
        var limpio = fields.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var nombre = string.IsNullOrWhiteSpace(clasif.NombresUsuario) ? (email.FromName ?? email.FromAddress ?? "sin nombre") : clasif.NombresUsuario;
        var tipo = string.IsNullOrWhiteSpace(clasif.TipoPqrs) ? "PQR" : clasif.TipoPqrs;

        var body = email.BodyText ?? "";
        if (body.Length > MaxBodyParaTarjeta) { body = body[..MaxBodyParaTarjeta] + "\n\n[...contenido truncado...]"; }
        var descripcion = new StringBuilder()
            .AppendLine($"Correo recibido de: {email.FromAddress}")
            .AppendLine($"Asunto: {email.Subject}")
            .AppendLine($"Fecha: {recibido:yyyy-MM-dd HH:mm}")
            .AppendLine()
            .AppendLine(body)
            .ToString();

        var card = new TaskCard
        {
            BoardId = boardId,
            ColumnId = columnId.Value,
            Title = $"{tipo} #{consecutivo} - {nombre}".Trim(),
            Description = descripcion,
            SortOrder = 0,
            IsArchived = false,
            FieldValuesJson = JsonSerializer.Serialize(limpio)
        };
        _db.TaskCards.Add(card);
        await _db.SaveChangesAsync(ct);

        // Arrastrar los adjuntos del correo a la tarjeta del tablero (se guardan en el mismo
        // almacenamiento que los adjuntos manuales del tablero y se enlazan como TaskCardAttachment).
        var adjuntos = 0;
        foreach (var att in email.Attachments)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var ext = Path.GetExtension(att.FileName);
                var storedName = $"pqr-{Guid.NewGuid():N}{ext}";
                var storedUrl = await _uploads.GuardarAsync("tableros", storedName, att.Content, ct);
                _db.TaskCardAttachments.Add(new TaskCardAttachment
                {
                    TenantId = card.TenantId,
                    TaskCardId = card.Id,
                    FileName = att.FileName,
                    Url = storedUrl,
                    MimeType = att.MimeType,
                    SizeBytes = att.Content.LongLength,
                    UploadedBy = null,
                    UploadedByName = "Correo PQR",
                });
                adjuntos++;
            }
            catch { /* un adjunto ilegible no debe tumbar la creacion de la tarjeta */ }
        }
        if (adjuntos > 0) { await _db.SaveChangesAsync(ct); }

        return (card.Id, adjuntos);
    }

    private async Task<int> SiguienteConsecutivoAsync(Guid boardId, CancellationToken ct)
    {
        var jsons = await _db.TaskCards.Where(c => c.BoardId == boardId && c.FieldValuesJson != null)
            .Select(c => c.FieldValuesJson!).ToListAsync(ct);
        var max = 0;
        foreach (var j in jsons)
        {
            try
            {
                using var doc = JsonDocument.Parse(j);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("n_consecutivo", out var el))
                {
                    var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > max) { max = n; }
                }
            }
            catch { /* json invalido: ignorar */ }
        }
        return max + 1;
    }

    /// <summary>Extrae el primer objeto JSON de la respuesta del agente (tolera fences ```json y prosa).</summary>
    internal static EmailPqrClassification? ParseClassification(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) { return null; }
        var json = raw.Substring(start, end - start + 1);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return null; }

            return new EmailPqrClassification(
                EsPqr: ReadBool(root, "es_pqr"),
                TipoPqrs: ReadStr(root, "tipo", "tipo_pqrs", "tipo_pqrs_f"),
                ServicioPqrs: ReadStr(root, "servicio", "servicio_pqrs"),
                DescripcionPqrsf: ReadStr(root, "descripcion", "descripcion_pqrsf", "resumen"),
                NombresUsuario: ReadStr(root, "nombres", "nombres_usuario", "nombre"),
                Identificacion: ReadStr(root, "identificacion", "documento", "cedula"),
                Celular: ReadStr(root, "celular", "telefono"),
                Email: ReadStr(root, "email", "correo"),
                AtributoCalidad: ReadStr(root, "atributo_calidad", "atributo"),
                RawJson: json);
        }
        catch { return null; }
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) { return false; }
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => el.GetString() is { } s &&
                (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("sí", StringComparison.OrdinalIgnoreCase) || s == "1"),
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            _ => false
        };
    }

    private static string? ReadStr(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var el))
            {
                var v = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ValueKind == JsonValueKind.Null ? null : el.ToString();
                if (!string.IsNullOrWhiteSpace(v)) { return v!.Trim(); }
            }
        }
        return null;
    }
}
