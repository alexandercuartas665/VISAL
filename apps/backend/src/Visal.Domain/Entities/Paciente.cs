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
    public DateOnly? FechaComentan { get; set; }

    public Guid? AseguradoraId { get; set; }
    public Aseguradora? Aseguradora { get; set; }

    public DateOnly? FechaIngresoPad { get; set; }
    public DateOnly? FechaEgresoPad { get; set; }
    /// <summary>Dias de estancia (cache); puede recalcularse desde FechaIngresoPad/FechaEgresoPad.</summary>
    public int? DiasEstancia { get; set; }
    /// <summary>Opcion de ingreso (dias autorizados por la aseguradora).</summary>
    public int? OpIngresoDias { get; set; }

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

    // ===== Contratos (FK a catalogos_paciente tipo=Contrato) =====
    public Guid? Contrato1Id { get; set; }
    public Guid? Contrato2Id { get; set; }
    public Guid? Contrato3Id { get; set; }

    // ===== Diagnostico =====
    /// <summary>FK a tabla local cie10_diagnosticos (deprecated: usar Cie10Codigo).</summary>
    public Guid? Cie10Id { get; set; }
    /// <summary>Codigo CIE-11/CIE-10 (texto). Viene del WHO ICD-11 API. Ej: "5A11", "I10".</summary>
    public string? Cie10Codigo { get; set; }
    public string? DiagnosticoPrincipal { get; set; }

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

    // ===== Estado del registro =====
    public bool Activo { get; set; } = true;
}
