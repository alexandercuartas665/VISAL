using System.Threading.Channels;
using Visal.Application.Revision.Ia;

namespace Visal.Infrastructure.Revision;

/// <summary>
/// Ola 8 RC8e — implementacion in-process de <see cref="IPreRevisionIaQueue"/>
/// sobre <c>System.Threading.Channels</c>. Cola unbounded: preferimos que la
/// memoria crezca antes que perder trabajo (el orquestador es idempotente y el
/// operador puede reintentar manualmente si se pierde). Un ceiling estilo drop
/// se puede agregar despues; por ahora la carga esperada es baja (docenas por
/// hora por tenant como maximo).
///
/// Registrado como Singleton porque el <see cref="Channel{T}"/> es thread-safe
/// y sobrevive tanto al request HTTP que encola como al worker que consume.
/// </summary>
public sealed class PreRevisionIaQueue : IPreRevisionIaQueue
{
    private readonly Channel<PreRevisionIaJob> _channel;

    public PreRevisionIaQueue()
    {
        _channel = Channel.CreateUnbounded<PreRevisionIaJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ValueTask EnqueueAsync(PreRevisionIaJob job, CancellationToken ct = default)
    {
        return _channel.Writer.WriteAsync(job, ct);
    }

    /// <summary>Solo lo llama el worker. Bloquea hasta que hay un item o el ct se cancela.</summary>
    public IAsyncEnumerable<PreRevisionIaJob> ReadAllAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
