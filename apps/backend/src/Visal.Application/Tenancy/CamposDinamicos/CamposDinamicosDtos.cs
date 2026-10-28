using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.CamposDinamicos;

// ---- Tabs (fichas) ----

public sealed record PacienteFichaDto(
    Guid Id, string TabKey, string Titulo, string? Descripcion, string? Color,
    int Orden, bool Oculto, bool EsSistema);

public sealed record CrearFichaRequest(string Titulo, string? Color, string? Descripcion);
public sealed record ActualizarFichaRequest(string Titulo, string? Color, string? Descripcion, bool Oculto);

// ---- Campos ----

public sealed record PacienteCampoDto(
    Guid Id, string TabKey, string CampoKey, string Label, PacienteCampoTipo Tipo,
    string? Opciones, int Ancho, int Orden, string? Descripcion, bool Requerido,
    bool Activo, bool EsSistema);

public sealed record CrearCampoRequest(
    string TabKey, string Label, PacienteCampoTipo Tipo, string? Opciones,
    int Ancho, string? Descripcion, bool Requerido);

public sealed record ActualizarCampoRequest(
    string Label, PacienteCampoTipo Tipo, string? Opciones, int Ancho,
    string? Descripcion, bool Requerido, bool Activo);

public sealed record ReordenarCamposRequest(IReadOnlyList<Guid> OrdenIds);

// ---- Campos FIJOS (colocacion) ----

/// <summary>
/// Colocacion de un campo fijo fusionada con su definicion de catalogo. <see cref="CampoKey"/> y
/// <see cref="Grupo"/>/<see cref="Label"/> salen del catalogo; el resto es la fila del tenant.
/// </summary>
public sealed record PacienteCampoRigidoDto(
    string CampoKey, string Label, string Grupo, string TabKey, int Orden, int Ancho,
    bool Oculto, bool Compuesto, bool Ocultable);

/// <summary>Un item del orden unificado de un tab (campo fijo o dinamico).</summary>
public sealed record ItemColocacion(bool EsRigido, Guid? CampoDefId, string? CampoKey);
