using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Relacion N:M entre paciente y contrato de aseguradora. Reemplaza los slots
/// fijos <c>Paciente.Contrato1Id/2Id/3Id</c> por una lista ordenable de contratos,
/// que ademas pueden pertenecer a aseguradoras distintas (via la FK a
/// <see cref="ContratoAseguradora"/> que ya lleva su propia
/// <c>AseguradoraId</c>).
///
/// <para><b>Reglas:</b></para>
/// <list type="bullet">
///   <item>Un paciente puede tener N contratos (sin cap).</item>
///   <item>Puede tener dos contratos de la misma aseguradora si son codigos
///         distintos — la unica es (paciente_id, contrato_aseguradora_id).</item>
///   <item><see cref="Orden"/> arranca en 1 y sirve para preservar el orden
///         elegido por el admin. El primero (orden=1) es el "default" que
///         auto-selecciona /asignacion.</item>
/// </list>
/// </summary>
public class PacienteContrato : TenantEntity
{
    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    public Guid ContratoAseguradoraId { get; set; }
    public ContratoAseguradora? ContratoAseguradora { get; set; }

    /// <summary>1..N; el orden=1 es el default en /asignacion.</summary>
    public int Orden { get; set; }
}
