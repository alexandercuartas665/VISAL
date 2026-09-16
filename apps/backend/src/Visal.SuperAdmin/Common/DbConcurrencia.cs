namespace Visal.SuperAdmin.Common;

/// <summary>
/// Mitigacion del error "A second operation was started on this context instance"
/// (InvalidOperationException de EF Core) propio de Blazor Server: el DbContext es
/// scoped y vive por todo el circuito, asi que dos operaciones que se solapan (navegar
/// con una consulta en vuelo, dos cargas rapidas) chocan. Reintenta con backoff corto;
/// como el otro flujo termina rapido, el reintento resuelve el choque sin mostrar error.
///
/// Es una mitigacion, no el arreglo de fondo (ese es IDbContextFactory por operacion).
/// </summary>
public static class DbConcurrencia
{
    private const int IntentosPorDefecto = 4;

    public static async Task<T> ReintentarAsync<T>(Func<Task<T>> op, int intentos = IntentosPorDefecto)
    {
        for (int i = 0; i < intentos - 1; i++)
        {
            try { return await op(); }
            catch (InvalidOperationException ex) when (EsChoqueDeContexto(ex))
            {
                await Task.Delay(120 * (i + 1));
            }
        }
        return await op();
    }

    public static Task ReintentarAsync(Func<Task> op, int intentos = IntentosPorDefecto)
        => ReintentarAsync(async () => { await op(); return true; }, intentos);

    private static bool EsChoqueDeContexto(InvalidOperationException ex)
        => ex.Message.Contains("second operation", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("concurrently using the same instance", StringComparison.OrdinalIgnoreCase);
}
