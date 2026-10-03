using Microsoft.Extensions.Caching.Memory;

namespace Visal.SuperAdmin.Facturacion;

/// <summary>
/// Tokens de un solo uso para renderizar una HC en PDF via Puppeteer. El generador
/// de Cuenta Medica mintea un token (ligado a HcId + TenantId), Puppeteer navega a
/// la ruta publica /p/hc-form/{HcId}?t={token}, y el render valida/consume el token.
/// En memoria (IMemoryCache), TTL corto, de un solo uso: una vez consumido se borra.
/// Es seguro porque: (1) el token es un GUID impredecible, (2) expira en minutos,
/// (3) solo autoriza VER el formulario de UNA HC de UN tenant, (4) single-use.
/// </summary>
public interface IHcPrintTokenStore
{
    /// <summary>Crea un token para imprimir la HC indicada bajo ese tenant.</summary>
    string Mint(Guid hcId, Guid tenantId);

    /// <summary>Valida y CONSUME el token (single-use). Null si no existe/expiro.</summary>
    (Guid HcId, Guid TenantId)? Consume(string token);
}

public sealed class HcPrintTokenStore(IMemoryCache cache) : IHcPrintTokenStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(3);

    private static string Key(string token) => "hcprint:" + token;

    public string Mint(Guid hcId, Guid tenantId)
    {
        var token = Guid.NewGuid().ToString("N");
        cache.Set(Key(token), (hcId, tenantId), Ttl);
        return token;
    }

    public (Guid HcId, Guid TenantId)? Consume(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) { return null; }
        var key = Key(token);
        if (cache.TryGetValue(key, out (Guid HcId, Guid TenantId) data))
        {
            cache.Remove(key); // single-use
            return data;
        }
        return null;
    }
}
