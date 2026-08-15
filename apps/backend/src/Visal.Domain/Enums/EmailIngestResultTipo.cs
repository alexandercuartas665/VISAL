namespace Visal.Domain.Enums;

/// <summary>Resultado del procesamiento de un correo por la ingesta de PQR.</summary>
public enum EmailIngestResultTipo
{
    /// <summary>El agente lo clasifico como PQR y se creo la tarjeta en el tablero.</summary>
    CreadaPqr = 0,

    /// <summary>El agente determino que el correo NO es una PQR; se descarto.</summary>
    NoEsPqr = 1,

    /// <summary>El correo ya se habia procesado antes (dedup por Message-ID).</summary>
    Duplicado = 2,

    /// <summary>Error al procesar (IMAP, IA o creacion de tarjeta).</summary>
    Error = 3,

    /// <summary>La configuracion no tiene agente clasificador o tablero destino.</summary>
    SinConfig = 4
}
