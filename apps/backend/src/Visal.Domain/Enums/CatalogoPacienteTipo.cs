namespace Visal.Domain.Enums;

/// <summary>
/// Discriminador para los catalogos del modulo de Configuracion de Pacientes.
/// Cada valor corresponde a una "lista" usada en el formulario de admision.
/// </summary>
public enum CatalogoPacienteTipo
{
    /// <summary>Tipos de usuario (contributivo, beneficiario, etc).</summary>
    TipoUsuario,
    /// <summary>Clasificacion del paciente (cronico, agudo, paliativo, etc).</summary>
    ClasificacionPaciente,
    /// <summary>Clasificacion de grupo de patologia (cardiovascular, respiratorio, etc).</summary>
    ClasificacionGrupoPatologia,
    /// <summary>Tipo de tutela (juzgado, derecho de peticion, accion, etc).</summary>
    TipoTutela,
    /// <summary>Contrato comercial entre IPS y aseguradora.</summary>
    Contrato,
    /// <summary>Mediamento contratado (SI/NO o detalle del contrato del medicamento).</summary>
    MedContratado
}
