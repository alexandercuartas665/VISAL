namespace Visal.SuperAdmin.Facturacion;

/// <summary>Estado de un job de generacion de ZIP de tipologia.</summary>
public enum ZipJobEstado { Ejecutando, Listo, Error }

/// <summary>Estado observable de un job de ZIP (para que la UI muestre progreso y, al
/// terminar, dispare la descarga del resultado ya cacheado en memoria).</summary>
public sealed class ZipJobInfo
{
    public Guid Id { get; init; }
    public ZipJobEstado Estado { get; set; } = ZipJobEstado.Ejecutando;
    public string Fase { get; set; } = "Preparando";
    public int Hechos { get; set; }
    public int Total { get; set; }
    public string? Error { get; set; }
    public string? NombreArchivo { get; set; }
    public byte[]? Resultado { get; set; }
    public DateTimeOffset Creado { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Terminado { get; set; }
}

/// <summary>
/// Corre la generacion del ZIP de una tipologia EN BACKGROUND (desacoplada del request
/// HTTP), para que archivos grandes (cientos de HC que se renderizan a PDF) no mueran por
/// timeout del navegador/proxy. La UI arranca el job, consulta su progreso y, cuando esta
/// listo, descarga el resultado desde un endpoint que solo sirve los bytes cacheados.
/// Singleton: los jobs viven en memoria del proceso (un solo server).
/// </summary>
public interface ITipologiaZipJobService
{
    /// <summary>Arranca un job y devuelve su id. El tenant va explicito porque el job corre
    /// fuera del request (sin HttpContext): se fija via TenantAmbient en el hilo de fondo.</summary>
    Guid Iniciar(Guid snapshotId, Guid archivoItemId, Guid tenantId, Guid? userId, Guid? sucursalId);

    /// <summary>Estado actual del job (o null si no existe / ya se purgo).</summary>
    ZipJobInfo? Obtener(Guid jobId);

    /// <summary>Bytes + nombre del ZIP si el job termino OK; null en otro caso.</summary>
    (byte[] Bytes, string Nombre)? TomarResultado(Guid jobId);
}
