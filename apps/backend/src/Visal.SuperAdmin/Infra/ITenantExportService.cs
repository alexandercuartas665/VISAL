namespace Visal.SuperAdmin.Infra;

/// <summary>
/// Export de autoservicio de los datos de UNA agencia (tenant). Tenant-safe por
/// construccion: recorre todas las tablas que tienen columna tenant_id y exporta SOLO
/// las filas de ese tenant (un CSV por tabla, dentro de un ZIP). Nunca incluye datos
/// de otras agencias.
/// </summary>
public interface ITenantExportService
{
    Task<byte[]> ExportarZipAsync(Guid tenantId, CancellationToken ct = default);
}
