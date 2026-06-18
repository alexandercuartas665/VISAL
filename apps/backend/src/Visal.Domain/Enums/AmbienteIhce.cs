namespace Visal.Domain.Enums;

/// <summary>
/// Ambiente al que apuntan las credenciales de interoperabilidad IHCE / RDA
/// (Resolucion 1888 de 2025). El operador configura ambos para sandbox y produccion
/// y elige cual esta activo al momento de enviar.
/// </summary>
public enum AmbienteIhce
{
    Sandbox = 0,
    Produccion = 1
}
