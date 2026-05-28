using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion del cliente WHO ICD-11 API por tenant. Almacena las credenciales OAuth2
/// (client_credentials) y los endpoints de busqueda + detalle. El servicio HTTP las usa
/// para obtener el token Bearer y consultar diagnosticos.
///
/// Claves (1:1 con el modulo legacy de Visal):
/// - TokenUrl: endpoint de auth (https://icdaccessmanagement.who.int/connect/token)
/// - ClientId / ClientSecret: credenciales del tenant en el portal WHO
/// - SearchUrl: GET con query q + useFlexisearch (https://id.who.int/icd/entity/search)
/// - MmsUrlBase: base para resolver detalle por entityId
///   (https://id.who.int/icd/release/11/2024-01/mms/)
/// </summary>
public class Cie11Config : TenantEntity
{
    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? SearchUrl { get; set; }
    public string? MmsUrlBase { get; set; }
    public bool Activo { get; set; } = true;
}
