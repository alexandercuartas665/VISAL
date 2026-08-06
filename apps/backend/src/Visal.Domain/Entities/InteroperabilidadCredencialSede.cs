using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Credenciales IHCE por sede (sucursal) y ambiente (sandbox / produccion).
/// Cada sede del prestador recibe su propio token IHCE — la consola IHCE Manager las
/// asigna por (codigo de habilitacion REPS, ambiente). El operador las copia aqui.
///
/// El ClientSecret se persiste cifrado con ASP.NET Data Protection. Nunca loggear.
/// </summary>
public class InteroperabilidadCredencialSede : TenantEntity
{
    public Guid SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }

    public AmbienteIhce Ambiente { get; set; }

    // -- Identidad del prestador en el ambiente (10 digitos REPS)
    public string? CodigoHabilitacion { get; set; }
    public string? NombreLlave { get; set; }

    // Numero de sede fisica dentro del REPS (default 01). MinSalud lo usa en
    // Location con patron `#{REPS}-{NumeroSede:D2}` (ej. `#7300103531-01`).
    // Correo04 (2026-08-06): el Custodian queda con REPS puro; el Location
    // es donde se especifica la sede efectiva donde se presto el servicio.
    public int NumeroSede { get; set; } = 1;

    // -- Credenciales OAuth2 client_credentials
    public string? ClientId { get; set; }
    public string? ClientSecretCifrado { get; set; }

    public DateTimeOffset? FechaExpiracion { get; set; }
}
