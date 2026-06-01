using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Registro del Codigo Unico de Medicamentos (CUM) - INVIMA Colombia.
/// Una fila representa una presentacion comercial concreta de un medicamento.
/// Cargado en bloque desde el Excel oficial del INVIMA o creado a mano.
/// Tenant-scoped: cada agencia mantiene su propia copia de la BD.
/// </summary>
public class Medicamento : TenantEntity
{
    // -------- Registro sanitario (cabecera del producto) --------
    public string? Expediente { get; set; }
    public string? Producto { get; set; }
    public string? Titular { get; set; }
    public string? RegistroSanitario { get; set; }
    public DateOnly? FechaExpedicion { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public string? EstadoRegistro { get; set; }

    // -------- CUM (presentacion comercial concreta) --------
    public string? ExpedienteCum { get; set; }
    public string? ConsecutivoCum { get; set; }
    public string? CantidadCum { get; set; }
    public string? DescripcionComercial { get; set; }
    public string? EstadoCum { get; set; }
    public DateOnly? FechaActivo { get; set; }
    public DateOnly? FechaInactivo { get; set; }
    public string? MuestraMedica { get; set; }
    public string? Unidad { get; set; }

    // -------- ATC + farmacologico --------
    public string? Atc { get; set; }
    public string? DescripcionAtc { get; set; }
    public string? ViaAdministracion { get; set; }
    public string? Concentracion { get; set; }
    public string? PrincipioActivo { get; set; }
    public string? UnidadMedida { get; set; }
    public string? Cantidad { get; set; }
    public string? UnidadReferencia { get; set; }
    public string? FormaFarmaceutica { get; set; }

    // -------- Rol / modalidad / IUM --------
    public string? NombreRol { get; set; }
    public string? TipoRol { get; set; }
    public string? Modalidad { get; set; }
    public string? Ium { get; set; }
}
