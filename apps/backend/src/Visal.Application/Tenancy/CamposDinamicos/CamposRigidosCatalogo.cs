namespace Visal.Application.Tenancy.CamposDinamicos;

/// <summary>
/// Definicion en codigo de un campo FIJO de la ficha de admision. Es la fuente de verdad del
/// label, grupo (subcabecera), tab y ancho por defecto, y de si el campo es un bloque COMPUESTO
/// (no redimensionable) o si es OCULTABLE. La personalizacion del tenant (tab/orden/ancho/oculto)
/// vive en <see cref="Visal.Domain.Entities.PacienteCampoRigidoDef"/>.
/// </summary>
public sealed record RigidoCampoInfo(
    string Key, string Label, string TabDefault, string Grupo,
    int AnchoDefault, bool Compuesto, bool Ocultable);

/// <summary>
/// Catalogo de los campos fijos de la ficha de admision, en el mismo orden en que se renderizan
/// hoy en Admision.razor. El orden de esta lista define el Orden por defecto dentro de cada tab.
/// Las claves empatan con los <c>RenderFragment</c> nombrados del formulario.
/// </summary>
public static class CamposRigidosCatalogo
{
    public const string TabGenerales = "generales";
    public const string TabAdicionales = "adicionales";

    public static readonly IReadOnlyList<RigidoCampoInfo> Todos = new List<RigidoCampoInfo>
    {
        // ----- generales / Identificacion -----
        new("numero_documento", "Numero Documento", TabGenerales, "Identificacion", 1, false, false),
        new("tipo_documento", "Tipo Documento", TabGenerales, "Identificacion", 1, false, true),
        new("primer_nombre", "Primer Nombre", TabGenerales, "Identificacion", 1, false, true),
        new("segundo_nombre", "Segundo Nombre", TabGenerales, "Identificacion", 1, false, true),
        new("primer_apellido", "Primer Apellido", TabGenerales, "Identificacion", 1, false, true),
        new("segundo_apellido", "Segundo Apellido", TabGenerales, "Identificacion", 1, false, true),
        new("fecha_nacimiento", "Fecha Nacimiento", TabGenerales, "Identificacion", 1, false, true),
        new("edad", "Edad", TabGenerales, "Identificacion", 1, false, true),

        // ----- generales / Datos administrativos PAD -----
        new("ips_comenta", "IPS Que Comenta", TabGenerales, "Datos administrativos PAD", 1, false, true),
        new("codigo_aceptacion", "Codigo de Aceptacion", TabGenerales, "Datos administrativos PAD", 1, false, true),
        new("fecha_comentan", "Fecha Comentan", TabGenerales, "Datos administrativos PAD", 1, false, true),
        new("entidad_aseguradora", "Entidad Aseguradora", TabGenerales, "Datos administrativos PAD", 1, false, true),
        new("fecha_ingreso_pad", "FECHA INGRESO PAD", TabGenerales, "Datos administrativos PAD", 1, false, true),
        new("fecha_egreso_pad", "FECHA EGRESO PAD", TabGenerales, "Datos administrativos PAD", 1, false, true),
        new("dias_estancia", "Dias Estancia", TabGenerales, "Datos administrativos PAD", 1, false, true),
        new("op_ingreso", "Op. de Ingreso (dias)", TabGenerales, "Datos administrativos PAD", 1, false, true),

        // ----- generales / Clasificacion -----
        new("incapacidad", "Incapacidad", TabGenerales, "Clasificacion", 1, false, true),
        new("grupo_rh", "Grupo RH", TabGenerales, "Clasificacion", 1, false, true),
        new("tipo_usuario", "Tipo Usuario", TabGenerales, "Clasificacion", 1, false, true),
        new("estado", "Estado", TabGenerales, "Clasificacion", 1, false, true),
        new("clasif_paciente", "Clasificacion Paciente", TabGenerales, "Clasificacion", 1, false, true),
        new("clasif_grupo_patologia", "Clasificacion Grupo de Patologia", TabGenerales, "Clasificacion", 1, false, true),
        new("contratos", "Contratos del paciente", TabGenerales, "Clasificacion", 3, true, true),
        new("estrato", "Estrato Social", TabGenerales, "Clasificacion", 1, false, true),
        new("sexo", "Sexo", TabGenerales, "Clasificacion", 1, false, true),
        new("zona", "Zona", TabGenerales, "Clasificacion", 1, false, true),

        // ----- generales / Diagnostico -----
        new("cie10", "CIE10 / CIE-11", TabGenerales, "Diagnostico", 1, true, true),
        new("dx_principal", "Diagnostico Principal", TabGenerales, "Diagnostico", 2, false, true),
        new("tutela", "Tutela", TabGenerales, "Diagnostico", 1, false, true),
        new("tipo_tutela", "Tipo Tutela", TabGenerales, "Diagnostico", 1, false, true),
        new("med_contratado", "Med Contratado", TabGenerales, "Diagnostico", 1, false, true),

        // ----- generales / Geografia y contacto -----
        new("pais_residencia", "Pais Residencia", TabGenerales, "Geografia y contacto", 1, false, true),
        new("pais_origen", "Pais Origen", TabGenerales, "Geografia y contacto", 1, false, true),
        new("departamento", "Departamento", TabGenerales, "Geografia y contacto", 1, false, true),
        new("municipio", "Municipio", TabGenerales, "Geografia y contacto", 1, false, true),
        new("direccion", "Direccion", TabGenerales, "Geografia y contacto", 2, false, true),
        new("barrio", "Barrio", TabGenerales, "Geografia y contacto", 1, false, true),
        new("ciudad", "Ciudad", TabGenerales, "Geografia y contacto", 1, false, true),
        new("telefonos", "Telefonos", TabGenerales, "Geografia y contacto", 3, true, true),
        new("email", "Correo Electronico", TabGenerales, "Geografia y contacto", 1, false, true),
        new("sede_atencion", "Sede Atencion", TabGenerales, "Geografia y contacto", 1, false, true),

        // ----- adicionales / Estado civil, ocupacion, regimen -----
        new("estado_civil", "Estado civil", TabAdicionales, "Estado civil, ocupacion, regimen", 1, false, true),
        new("ocupacion", "Ocupacion", TabAdicionales, "Estado civil, ocupacion, regimen", 1, false, true),
        new("regimen", "Regimen", TabAdicionales, "Estado civil, ocupacion, regimen", 1, false, true),

        // ----- adicionales / Contactos de emergencia -----
        new("contactos_emergencia", "Contactos de emergencia", TabAdicionales, "Contactos de emergencia", 3, true, true),
        new("activo", "Activo", TabAdicionales, "Contactos de emergencia", 1, false, true),
    };

    private static readonly Dictionary<string, RigidoCampoInfo> _porKey =
        Todos.ToDictionary(c => c.Key, StringComparer.Ordinal);

    public static RigidoCampoInfo? Find(string key) =>
        _porKey.TryGetValue(key, out var info) ? info : null;
}
