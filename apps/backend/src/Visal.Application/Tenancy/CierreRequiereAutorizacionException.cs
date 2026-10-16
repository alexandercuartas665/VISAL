namespace Visal.Application.Tenancy;

/// <summary>
/// Se lanza desde <c>HistoriaClinicaService.CerrarAsync</c> cuando la fecha de atencion
/// de la HC excede el limite de oportunidad del tipo de servicio y la HC no tiene una
/// autorizacion (token) vigente que la cubra. Aborta el cierre ANTES de mutar estado o
/// persistir. La UI la captura para abrir el modal de token y reintentar el cierre.
/// </summary>
public sealed class CierreRequiereAutorizacionException : Exception
{
    public CierreRequiereAutorizacionException(string mensaje) : base(mensaje) { }
}
