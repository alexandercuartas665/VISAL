using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Resultado de construir un Bundle FHIR RDA desde una HistoriaClinica.
/// </summary>
public sealed record RdaBuildResult(
    Guid RdaEventoId,
    string BundleJson,
    string BundleHash,
    EstadoRdaEvento Estado,
    int RecursosCount,
    bool YaExistia,
    IReadOnlyList<string> Advertencias);

/// <summary>
/// Construye Bundles FHIR R4 (perfil minsalud.fhir.co.rda v1.0.0) a partir de
/// HistoriasClinicas de Visal. Persiste el resultado como <see cref="Visal.Domain.Entities.RdaEvento"/>
/// en estado <see cref="EstadoRdaEvento.Borrador"/>.
///
/// Ola 2 — solo arma los recursos demograficos / de contexto:
/// Composition + Patient + Encounter + Practitioner + Organization. Las secciones
/// clinicas (Condition / MedicationStatement / Procedure / AllergyIntolerance) se llenan
/// en la Ola 3.
/// </summary>
public interface IRdaBuilderService
{
    /// <summary>
    /// Construye el Bundle RDA para una HC y lo persiste como RdaEvento.
    /// Si ya existe un evento con el mismo hash, devuelve el existente sin duplicar.
    /// </summary>
    Task<RdaBuildResult> ConstruirAsync(Guid historiaClinicaId, ModalidadRdaIhce modalidad, Guid actor, CancellationToken ct = default);
}
