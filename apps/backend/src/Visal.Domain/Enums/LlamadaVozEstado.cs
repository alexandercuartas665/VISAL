namespace Visal.Domain.Enums;

/// <summary>Ciclo de vida de una llamada de voz IA (Retell).</summary>
public enum LlamadaVozEstado
{
    /// <summary>Creada en Retell, aun no timbra.</summary>
    Registrada = 1,
    /// <summary>En curso (call_started).</summary>
    EnCurso = 2,
    /// <summary>Terminada (call_ended) — puede haber contestado o no.</summary>
    Finalizada = 3,
    /// <summary>Analisis post-llamada listo (call_analyzed).</summary>
    Analizada = 4,
    /// <summary>Fallo al crear/enviar o error del proveedor.</summary>
    Error = 5,
    /// <summary>La llamada no fue contestada / buzon / rechazada.</summary>
    NoContactado = 6,
}
