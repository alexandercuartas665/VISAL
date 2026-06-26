using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Snapshot inmutable del estado anterior de una FormDefinition. Cada UPDATE
/// del formulario genera una fila aqui via trigger Postgres BEFORE UPDATE
/// (ver migracion AddFormDefinitionSnapshots), atrapando cambios provenientes
/// de cualquier ruta: UI del FormBuilder, scripts PowerShell, SQL directo,
/// futuras migrations. La rotacion conserva solo los 20 mas recientes por
/// form_definition_id.
///
/// Permite revertir cambios accidentales — incluido el caso de un script que
/// sobreescribe modificaciones manuales en produccion. La tabla es
/// tenant-scoped (TenantEntity) para que el query filter global no exponga
/// snapshots de otros tenants.
/// </summary>
public class FormDefinitionSnapshot : TenantEntity
{
    /// <summary>FK a la fila viva de form_definitions. ON DELETE CASCADE: si el
    /// formulario se borra, sus snapshots se borran con el.</summary>
    public Guid FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }

    // ── Copia exacta del estado anterior al UPDATE ──
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Version { get; set; }
    public string? Tipo { get; set; }
    public string SchemaJson { get; set; } = "{}";
    public string? PrefillRoutesJson { get; set; }
    public bool Activo { get; set; }

    // ── Metadata del snapshot ──
    /// <summary>Cuando se genero el snapshot (= cuando se ejecuto el UPDATE).</summary>
    public DateTimeOffset SnapshotAt { get; set; }

    /// <summary>UserId del actor cuando se conoce. El trigger PG no tiene acceso
    /// al usuario logueado de la app, asi que para snapshots automaticos queda
    /// null. RestaurarAsync si setea este campo.</summary>
    public Guid? SnapshotBy { get; set; }

    /// <summary>Origen del snapshot: "auto-trigger" (por trigger PG en UPDATE),
    /// "restore" (cuando una restauracion genera el backup del estado actual),
    /// o el motivo que el caller indique. Texto libre, util para auditoria.</summary>
    public string? Motivo { get; set; }
}
