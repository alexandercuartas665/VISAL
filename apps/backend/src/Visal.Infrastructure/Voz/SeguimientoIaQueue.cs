using System.Threading.Channels;
using Visal.Application.Voz;

namespace Visal.Infrastructure.Voz;

/// <summary>
/// Implementacion in-process de <see cref="ISeguimientoIaQueue"/> sobre
/// <c>System.Threading.Channels</c> (patron <c>PreRevisionIaQueue</c>). Cola
/// unbounded: preferimos crecer en memoria antes que perder trabajo (el resolver
/// es idempotente: llena solo campos vacios). La carga esperada es baja (una por
/// llamada terminada). Singleton: el Channel sobrevive al webhook que encola y al
/// worker que consume.
/// </summary>
public sealed class SeguimientoIaQueue : ISeguimientoIaQueue
{
    private readonly Channel<SeguimientoIaJob> _channel;

    public SeguimientoIaQueue()
    {
        _channel = Channel.CreateUnbounded<SeguimientoIaJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ValueTask EnqueueAsync(SeguimientoIaJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    /// <summary>Solo lo llama el worker. Bloquea hasta que hay un item o el ct se cancela.</summary>
    public IAsyncEnumerable<SeguimientoIaJob> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
