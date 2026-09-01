using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Servicio asignado a un paciente dentro de un lote. Reemplaza la tabla legacy
/// VISAL_ASIGNACIONES del modulo Visal (000822). Tenant-scoped.
///
/// Reglas:
/// - Cantidad > 0 (validacion en dominio).
/// - mes_vigencia / mes_final son enteros 1..12 (no string).
/// - Estado nace Pendiente y lo evoluciona Coordinacion.
/// - Texto se decodifica en presentacion (no HTML en BD).
/// </summary>
public class Asignacion : TenantEntity
{
    public Guid LoteId { get; set; }
    public AsignacionLote? Lote { get; set; }

    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    public string Sucursal { get; set; } = null!;

    /// <summary>Id del servicio del catalogo (puede ser el GUID del ServicioContrato o el codigo string).</summary>
    public string ServicioId { get; set; } = null!;
    public string NombreServicio { get; set; } = null!;

    /// <summary>Codigo del servicio "limpio" para RIPS/CUPS: es el CodigoServicio del
    /// ServicioContrato SIN el sufijo de modalidad d/f (ej. "E891865d" -> "E891865").
    /// Se captura como snapshot al crear/editar la asignacion porque RIPS no maneja ese
    /// sufijo. Nullable por compatibilidad con asignaciones legacy (se recalcula al vuelo
    /// desde el ServicioContrato cuando falta). Ver <c>ServicioCodigo.Base</c>.</summary>
    public string? CodigoRips { get; set; }

    /// <summary>Tipo de servicio derivado de servicios_contrato.Modulo (CONSULTA/TERAPIA/ENFERMERIA/EQUIPOS/INSUMOS).</summary>
    public string TipoServicio { get; set; } = null!;

    /// <summary>Modulo (puede coincidir con TipoServicio en la mayoria de casos; se conserva para casos data-driven).</summary>
    public string? Modulo { get; set; }

    public int Cantidad { get; set; }

    public string ContratoCodigo { get; set; } = null!;

    /// <summary>Numero de orden / autorizacion de la aseguradora.</summary>
    public string? CodigoAutorizacion { get; set; }

    public short? AnioServicio { get; set; }
    public short MesVigencia { get; set; }
    public short? MesFinal { get; set; }

    public DateOnly FechaInicio { get; set; }
    public DateOnly? FechaFinal { get; set; }

    public string? Observaciones { get; set; }

    /// <summary>Formato de historia ligado al servicio (FormDefinition.Codigo, p.ej.).</summary>
    public string? FormatoHistoria { get; set; }

    /// <summary>Codigo RIPS de la Via de ingreso al servicio de salud (Res. 202/2021,
    /// tabla RIPS_VIA_INGRESO). Se captura al crear la asignacion y viaja a la HC
    /// como snapshot inmutable al iniciar la historia — antes se preguntaba en el
    /// modal RIPS de HC. Nullable en BD por compatibilidad con asignaciones legacy
    /// (backfill via HC mas reciente); el servicio lo exige not-null al guardar.</summary>
    public string? RipsViaIngresoCodigo { get; set; }

    /// <summary>Nombre RIPS de la Via de ingreso (denormalizado del catalogo para
    /// evitar JOIN en snapshots/reportes). Se guarda en paralelo al codigo.</summary>
    public string? RipsViaIngresoNombre { get; set; }

    /// <summary>URL / ruta relativa del PDF de autorizacion adjunto. Opcional; solo se
    /// vuelve obligatorio cuando el ContratoAseguradora.RequierePdfAutorizacion es true.</summary>
    public string? PdfAutorizacionUrl { get; set; }

    /// <summary>Marca que la asignacion se guardo SIN la autorizacion (numero y/o PDF)
    /// y queda a la espera de completarla. Cuando es true, el flujo de creacion no exige
    /// el PDF aunque el contrato lo pida; se completa despues desde el tab "Listado"
    /// (filtro "Autorizaciones pendientes" -> modal) y al completar se pone en false.</summary>
    public bool AutorizacionPendiente { get; set; }

    /// <summary>Tipo de pago del paciente: "CUOTA" (cuota moderadora), "COPAGO" o null si no aplica.</summary>
    public string? TipoPago { get; set; }

    /// <summary>Categoria/rango salarial del catalogo CuotaCopago que sugirio el valor.</summary>
    public string? CategoriaCopago { get; set; }

    /// <summary>Valor sugerido por el catalogo al momento de crear la asignacion.</summary>
    public decimal? ValorPagoSugerido { get; set; }

    /// <summary>Valor real que pago el paciente (puede diferir del sugerido).</summary>
    public decimal? ValorPagoReal { get; set; }

    public AsignacionEstado Estado { get; set; } = AsignacionEstado.Pendiente;

    // ---------------- Trazabilidad de paquete ----------------
    // Cuando la asignacion nace de aplicar un paquete en el flujo /asignacion,
    // estos tres campos vinculan la fila con las hermanas del mismo paquete:

    /// <summary>Guid unico que agrupa TODAS las asignaciones creadas al aplicar un mismo
    /// paquete a un paciente. Se genera en el frontend al elegir el servicio ancla; se
    /// stampa igual en todos los chips expandidos del carrito. Luego /coordinacion lo
    /// hereda a asignacion_turnos para poder GROUP BY paquete_instancia_id.</summary>
    public Guid? PaqueteInstanciaId { get; set; }

    /// <summary>Codigo del paquete de origen (snapshot). Se guarda por conveniencia
    /// para reportes y filtros — evita un JOIN a paquetes cada vez que se muestra el chip.</summary>
    public string? PaqueteCodigo { get; set; }

    /// <summary>Valor pactado del paquete completo. SOLO una fila del mismo
    /// <see cref="PaqueteInstanciaId"/> lleva el valor (la primera con Cantidad>0 en el
    /// orden de agregado); el resto queda null. Copiado de <c>Paquete.Precio</c> al aplicar.</summary>
    public decimal? PaqueteValorPactado { get; set; }
}
