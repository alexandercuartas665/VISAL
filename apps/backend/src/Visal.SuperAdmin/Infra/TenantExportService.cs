using System.Data;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Visal.Application.Common;

namespace Visal.SuperAdmin.Infra;

/// <summary>
/// Exporta los datos de un tenant a un ZIP de CSVs usando COPY ... TO STDOUT por tabla,
/// filtrando SIEMPRE por tenant_id. La lista de tablas sale del catalogo (todas las que
/// tienen columna tenant_id), asi cualquier tabla nueva tenant-scoped queda incluida sin
/// tocar este codigo. El tenantId lo fija el servidor (contexto de sesion), nunca el
/// cliente, por eso se puede interpolar como literal uuid sin riesgo de inyeccion.
/// </summary>
public sealed class TenantExportService : ITenantExportService
{
    private readonly IApplicationDbContextFactory _dbFactory;

    public TenantExportService(IApplicationDbContextFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<byte[]> ExportarZipAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        var abrir = conn.State != ConnectionState.Open;
        if (abrir) { await conn.OpenAsync(ct); }
        try
        {
            var tablas = await TablasTenantScopedAsync(conn, ct);

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var readme = zip.CreateEntry("LEEME.txt");
                using (var w = new StreamWriter(readme.Open()))
                {
                    await w.WriteAsync(
                        "Export de datos de la agencia.\r\n" +
                        $"Tenant: {tenantId}\r\n" +
                        $"Generado: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}\r\n" +
                        $"Tablas incluidas: {tablas.Count}.\r\n" +
                        "Cada archivo .csv contiene UNICAMENTE las filas de esta agencia.\r\n");
                }

                var tidLit = tenantId.ToString("D");
                foreach (var tabla in tablas)
                {
                    ct.ThrowIfCancellationRequested();
                    // Comillas dobles alrededor del nombre de tabla (viene del catalogo, no del usuario).
                    var copySql = $"COPY (SELECT * FROM \"{tabla}\" WHERE tenant_id = '{tidLit}'::uuid) TO STDOUT WITH (FORMAT csv, HEADER true)";
                    var entry = zip.CreateEntry($"{tabla}.csv");
                    using var es = entry.Open();
                    using var sw = new StreamWriter(es);
                    using var reader = conn.BeginTextExport(copySql);
                    var buf = new char[16384];
                    int n;
                    while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                    {
                        sw.Write(buf, 0, n);
                    }
                }
            }
            return ms.ToArray();
        }
        finally
        {
            if (abrir) { await conn.CloseAsync(); }
        }
    }

    private static async Task<List<string>> TablasTenantScopedAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var tablas = new List<string>();
        const string sql =
            "SELECT c.table_name FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name " +
            "WHERE c.table_schema = 'public' AND c.column_name = 'tenant_id' AND t.table_type = 'BASE TABLE' " +
            "ORDER BY c.table_name";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            tablas.Add(rd.GetString(0));
        }
        return tablas;
    }
}
