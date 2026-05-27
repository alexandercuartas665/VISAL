namespace Visal.Application.Common.Auth;

/// <summary>Configuracion del JWT propio de VISAL.travels (seccion "Jwt").</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "Visal";
    public string Audience { get; set; } = "Visal";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
}
