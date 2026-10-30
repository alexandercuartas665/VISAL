using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Paciente de la IPS (admision). Tenant-scoped. Raiz del dominio asistencial.
/// Estructura completa basada en la HC institucional de Visal IPS RT:
/// - Datos basicos (documento, nombres, fechas)
/// - Datos administrativos PAD (comentan, ingreso/egreso, contratos, dx)
/// - Geografia (pais residencia/origen, depto, municipio, sede atencion)
/// - Contacto y emergencia
/// Los campos FK Guid? (ips_comenta_id, contrato1_id, cie10_id, etc.) quedan como
/// cimientos para futuras tablas catalogo. Los selects de enumeracion (incapacidad,
/// grupo_rh, etc.) se almacenan como string libre por ahora.
/// </summary>
public class Paciente : TenantEntity
{
    // ===== Identificacion =====
    public string NumeroDocumento { get; set; } = null!;
    public string TipoDocumento { get; set; } = "CC";
    public string? PrimerNombre { get; set; }
    public string? SegundoNombre { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string NombreCompleto { get; set; } = null!;

    public DateOnly? FechaNacimiento { get; set; }
    /// <summary>Edad calculada al ingreso (cache para reportes); puede recalcularse desde FechaNacimiento.</summary>
    public int? Edad { get; set; }

    // ===== Datos administrativos PAD =====
    /// <summary>IPS que remite/comenta el paciente. FK a futura tabla ips_externas.</summary>
    public Guid? IpsComentaId { get; set; }
    public string? CodigoAceptacion { get; set; }
    /// <summary>Fecha y hora en que la IPS comenta/remite el paciente (CENSO). Se guarda con offset local (-5).</summary>
    public DateTimeOffset? FechaComentan { get; set; }
    /// <summary>Fecha y hora de atencion del paciente (CENSO). Se guarda con offset local (-5).</summary>
    public DateTimeOffset? FechaAtencion { get; set; }

    public Guid? AseguradoraId { get; set; }
    public Aseguradora? Aseguradora { get; set; }

    public DateOnly? FechaIngresoPad { get; set; }
    public DateOnly? FechaEgresoPad { get; set; }
    /// <summary>Dias de estancia (cache); puede recalcularse desde FechaIngresoPad/FechaEgresoPad.</summary>
    public int? DiasEstancia { get; set; }
    /// <summary>Opcion de ingreso (dias autorizados por la aseguradora).</summary>
    public int? OpIngresoDias { get; set; }

    /// <summary>
    /// Decision de ingreso al programa (hoja "Ingreso de Pacientes" del CENSO, capturada en
    /// Admision): true=SI ingresa, false=NO ingresa, null=sin definir.
    /// </summary>
    public bool? Ingreso { get; set; }
    /// <summary>Motivo por el cual el paciente NO ingresa. Solo aplica cuando <see cref="Ingreso"/> es false.</summary>
    public string? MotivoNoIngreso { get; set; }

    /// <summary>Usuario de la plataforma que registro el ingreso (hoja "Ingreso de Pacientes" del CENSO).
    /// Referencia al PlatformUser elegido en Admision. Null si no se asigno.</summary>
    public Guid? UsuarioIngresoId { get; set; }
    /// <summary>Nombre del usuario del ingreso, desnormalizado al guardar (igual que ResponsableLlamadaNombre
    /// en la encuesta): hace trivial el reporte por nombre y queda estable aunque el usuario cambie o se desactive.</summary>
    public string? UsuarioIngresoNombre { get; set; }

    /// <summary>Condicion del paciente (hoja "Ingreso de Pacientes" del CENSO). Valor de una lista fija
    /// (traqueostomia, gastrotomia, etc.); se almacena como texto.</summary>
    public string? Condicion { get; set; }
    /// <summary>SLA del ingreso en minutos: diferencia entre <see cref="FechaComentan"/> (la IPS comenta) y
    /// <see cref="FechaAtencion"/> (respuesta/atencion). Derivado: se calcula al guardar. Null si falta alguna fecha.</summary>
    public int? SlaIngresoMinutos { get; set; }

    // ===== Clasificaciones (texto libre + FK al modulo Configuracion Pacientes) =====
    public string? Incapacidad { get; set; }
    public string? GrupoRh { get; set; }
    /// <summary>FK a catalogos_paciente (tipo=TipoUsuario). Lista configurable por tenant.</summary>
    public Guid? TipoUsuarioId { get; set; }
    public string? Estado { get; set; }
    /// <summary>FK a catalogos_paciente (tipo=ClasificacionPaciente).</summary>
    public Guid? ClasificacionPacienteId { get; set; }
    /// <summary>FK a catalogos_paciente (tipo=ClasificacionGrupoPatologia).</summary>
    public Guid? ClasificacionGrupoPatologiaId { get; set; }
    public string? EstratoSocial { get; set; }
    public string? Sexo { get; set; }
    public string? EstadoCivil { get; set; }
    public string? Zona { get; set; }
    public string? Ocupacion { get; set; }
    public string? Regimen { get; set; }

    // ===== Contratos =====
    // (Los slots fijos Contrato1/2/3Id fueron reemplazados por la tabla N:M
    // paciente_contratos en PC-1..PC-4. Ver la entidad PacienteContrato.)

    /// <summary>Nombre de la aseguradora del contrato PRINCIPAL (el de Orden 1 en paciente_contratos),
    /// desnormalizado al guardar. Es solo-lectura (no se edita; se deriva de la lista de contratos) y
    /// existe para exponer "Contrato" como una columna unica en el CENSO (la tabla pivote no se puede
    /// cruzar como un FK). Null si el paciente no tiene contratos.</summary>
    public string? ContratoPrincipal { get; set; }

    // ===== Diagnostico =====
    /// <summary>FK a tabla local cie10_diagnosticos (deprecated: usar Cie10Codigo).</summary>
    public Guid? Cie10Id { get; set; }
    /// <summary>Codigo CIE-11/CIE-10 (texto). Viene del WHO ICD-11 API. Ej: "5A11", "I10".</summary>
    public string? Cie10Codigo { get; set; }
    public string? DiagnosticoPrincipal { get; set; }

    /// <summary>Codigo CIE-10/CIE-11 del diagnostico secundario (CENSO). Texto libre.</summary>
    public string? Cie10Codigo2 { get; set; }
    /// <summary>Descripcion del diagnostico secundario (CENSO).</summary>
    public string? DiagnosticoSecundario { get; set; }

    // ===== Tutela =====
    public string? Tutela { get; set; }
    /// <summary>FK a catalogos_paciente (tipo=TipoTutela).</summary>
    public Guid? TipoTutelaId { get; set; }
    /// <summary>FK a catalogos_paciente (tipo=MedContratado).</summary>
    public Guid? MedContratadoId { get; set; }

    // ===== Geografia (FKs a futuras tablas catalogo) =====
    public Guid? PaisResidenciaId { get; set; }
    public Guid? PaisOrigenId { get; set; }
    public Guid? DepartamentoId { get; set; }
    public Guid? MunicipioId { get; set; }
    public string? Direccion { get; set; }
    public string? Barrio { get; set; }
    public string? Ciudad { get; set; }

    // ===== Contacto =====
    /// <summary>Codigo de pais con prefijo "+". Default "+57" (Colombia).</summary>
    public string? CodigoPaisTelefono { get; set; } = "+57";
    public string? Telefono { get; set; }
    public string? Email { get; set; }

    // ===== Sede de atencion =====
    /// <summary>Sede de la IPS donde se atiende al paciente (FK a sucursales).</summary>
    public Guid? SedeAtencionId { get; set; }
    public Sucursal? SedeAtencion { get; set; }

    // ===== Campos dinamicos (gestor de campos del tenant) =====
    /// <summary>
    /// Valores de los campos dinamicos definidos por el tenant para la ficha de
    /// admision. jsonb PLANO: { "campoKey": "valor-string" } (no anidado por tab, para
    /// que mover un campo de tab no huerfane el valor y que el CENSO lo lea por clave).
    /// La definicion de cada campo vive en paciente_campo_defs.
    /// </summary>
    public string? CamposDinamicosJson { get; set; }

    // ===== Contacto de emergencia (legacy — primer contacto) =====
    /// <summary>Nombre del contacto principal. Se mantiene por compat: la lista
    /// completa vive en <see cref="ContactosEmergencia"/>.</summary>
    public string? ContactoEmergencia { get; set; }
    public string? Parentesco { get; set; }
    public string? TelefonoEmergencia { get; set; }

    /// <summary>Lista completa de contactos de emergencia (1..N). El primero
    /// se sincroniza con los 3 campos legacy de arriba para no romper consumidores
    /// que esperan un solo contacto.</summary>
    public List<PacienteContactoEmergencia> ContactosEmergencia { get; set; } = new();

    // ===== Reingreso / Hospitalizacion / Observaciones (CENSO - Ola 4) =====
    /// <summary>Fecha de reingreso del paciente al programa (CENSO).</summary>
    public DateOnly? FechaReingreso { get; set; }
    public string? ObservacionReingreso { get; set; }
    /// <summary>Fecha de hospitalizacion del paciente (CENSO).</summary>
    public DateOnly? FechaHospitalizacion { get; set; }
    public string? ObservacionesHospitalizacion { get; set; }
    /// <summary>Observaciones generales del paciente (notas libres a nivel paciente, CENSO).</summary>
    public string? Observaciones { get; set; }

    // ===== Estado del registro =====
    public bool Activo { get; set; } = true;

    /// <summary>Marca el paciente como "de prueba": permite gestionarlo desde el
    /// modulo Pacientes de prueba y limpiar toda su actividad clinica/operativa
    /// (historias, atenciones, notas, firmas, etc.) conservando el registro del
    /// paciente, su afiliacion y sus contactos de emergencia.</summary>
    public bool EsPrueba { get; set; }

    // ===== Estado de admision (Abierto / Cerrado) =====
    /// <summary>
    /// Estado del proceso de admision del paciente. Un paciente Abierto es un
    /// registro en captura: campos incompletos permitidos y editable. Un
    /// paciente Cerrado tiene todos los datos obligatorios diligenciados y ya
    /// se puede usar para asignaciones. Modulo Asignacion RECHAZA pacientes
    /// Abiertos (obliga a la coordinadora a completar los datos antes).
    /// Valores: "Abierto" (default) o "Cerrado".
    /// </summary>
    public string EstadoAdmision { get; set; } = "Abierto";

    /// <summary>Timestamp UTC del cierre; null mientras el paciente esta Abierto.</summary>
    public DateTimeOffset? FechaCierreAdmision { get; set; }
}
