using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Construye Bundles FHIR R4 RDA Consulta (perfil <c>CompositionAmbulatoryRDA</c>)
/// para el endpoint <c>$enviar-rda-consulta</c> de la API IHCE de MinSalud.
///
/// Diferencias respecto al <see cref="IRdaBuilderService"/> (RDA Paciente):
/// - Reporta UNA atencion clinica especifica (con Encounter), no antecedentes.
/// - Perfil Composition: <c>CompositionAmbulatoryRDA</c> (LOINC 51845-6).
/// - Incluye Encounter (con CUPS, REPS, diagnosis con rank, discharge disposition).
/// - Author es Organization (no Practitioner) y attester es Practitioner.
/// - Tiene 9 secciones: Pagador, Demograficos, Incapacidad, Dx, Alergias, Riesgo,
///   Meds (MedicationRequest), Servicios, Documentos (epicrisis PDF).
/// - El RdaEvento creado lleva <see cref="TipoRdaIhce.Consulta"/>.
/// </summary>
public interface IRdaConsultaBuilderService
{
    /// <summary>
    /// Construye el Bundle RDA Consulta para la HC indicada y lo persiste como RdaEvento.
    /// Si ya existe un evento con el mismo hash, devuelve el existente (idempotencia).
    /// </summary>
    Task<RdaBuildResult> ConstruirAsync(Guid historiaClinicaId, Guid actor, CancellationToken ct = default);
}
