using Visal.Application.Tenancy.WhatsApp;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Infrastructure.WhatsApp;

/// <summary>
/// Selecciona el IWhatsAppProvider correcto para una linea segun su campo
/// Provider. Registrado como scoped: los providers concretos comparten el
/// DbContext scoped, asi que este resolver debe estar en el mismo scope.
/// </summary>
internal sealed class WhatsAppProviderResolver : IWhatsAppProviderResolver
{
    private readonly EvolutionWhatsAppProvider _evolution;
    private readonly GupshupWhatsAppProvider _gupshup;

    public WhatsAppProviderResolver(
        EvolutionWhatsAppProvider evolution,
        GupshupWhatsAppProvider gupshup)
    {
        _evolution = evolution;
        _gupshup = gupshup;
    }

    public IWhatsAppProvider ForLine(WhatsAppLine line) => line.Provider switch
    {
        WhatsAppProvider.Evolution => _evolution,
        WhatsAppProvider.Gupshup => _gupshup,
        _ => throw new NotSupportedException($"Provider WhatsApp no soportado: {line.Provider}."),
    };
}
