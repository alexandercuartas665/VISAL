using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion global de interoperabilidad IHCE / RDA por tenant (1 fila por agencia).
/// Estos campos son los parametros tecnicos del API IHCE — son los mismos para todas
/// las sedes del prestador. Las credenciales por sede (CodigoHabilitacion + ClientID +
/// ClientSecret) van en <see cref="InteroperabilidadCredencialSede"/>.
///
/// Los secretos (APIMsubskey) se persisten cifrados con ASP.NET Data Protection. Nunca
/// loggear en claro ni exponer en logs / responses.
/// </summary>
public class InteroperabilidadConfig : TenantEntity
{
    // -- Endpoints del API IHCE (base URL sin "/" final)
    public string? EndpointSandbox { get; set; }
    public string? EndpointProduccion { get; set; }

    // -- Azure AD (mismo TenantID y Scope para sandbox y produccion segun la guia IHCE)
    public string? AzureTenantId { get; set; }
    public string? Scope { get; set; }

    // -- APIM subscription key (header Ocp-Apim-Subscription-Key). Cifradas en BD.
    public string? ApimSubskeySandboxCifrada { get; set; }
    public string? ApimSubskeyProduccionCifrada { get; set; }

    // -- Ambiente que se usara al enviar RDAs (Sandbox por defecto hasta que el
    //    operador confirme paso a produccion).
    public AmbienteIhce AmbienteActivo { get; set; } = AmbienteIhce.Sandbox;

    // -- Paths configurables de los servicios del API IHCE.
    // El sandbox de MinSalud expone operaciones FHIR custom usando notacion $operacion
    // sobre el recurso base (Composition, Patient, etc.). Los defaults son los que
    // estan en la coleccion Postman del MinSalud (junio 2026). Si MinSalud cambia el
    // path en una version futura del API, el operador lo ajusta aqui sin recompilar.
    public string PathEnvioRda { get; set; } = "/Composition/$enviar-rda-paciente";
    public string PathConsultarPaciente { get; set; } = "/Patient/$consultar-paciente-exacto";
    public string PathConsultarProfesional { get; set; } = "/Practitioner/$consultar-profesional-salud";
}
