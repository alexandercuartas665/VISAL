using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Visal.Application.Voz;
using Visal.Infrastructure.Voz;
using Xunit;

namespace Visal.Application.Tests.Voz;

/// <summary>
/// Tests del cliente HTTP de Retell con la API MOCKEADA (HttpMessageHandler stub).
/// NO hacen llamadas telefonicas ni pegan a la API real.
/// </summary>
public class RetellHttpClientTests
{
    private static RetellHttpClient Build(HttpMessageHandler handler)
        => new(new HttpClient(handler), new StubConfig(), NullLogger<RetellHttpClient>.Instance);

    private static CrearLlamadaRequest Req()
        => new("+15550001111", "+573001234567");

    [Fact]
    public async Task CrearLlamada_Exito_DevuelveCallId()
    {
        var handler = new StubHandler(HttpStatusCode.Created,
            """{ "call_id": "call_abc", "call_status": "registered" }""");
        var client = Build(handler);

        var r = await client.CrearLlamadaAsync(Req());

        Assert.True(r.Ok);
        Assert.Equal("call_abc", r.CallId);
        Assert.Equal("registered", r.CallStatus);
        Assert.Equal(1, handler.Count);
        // Auth por Bearer, sin filtrar la key en el request path.
        Assert.Equal("Bearer", handler.Last!.Headers.Authorization!.Scheme);
        Assert.Equal("sk_test_key", handler.Last.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task CrearLlamada_401_NoTransitorioYNoReintenta()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{ "message": "bad key" }""");
        var client = Build(handler);

        var r = await client.CrearLlamadaAsync(Req());

        Assert.False(r.Ok);
        Assert.False(r.Transitorio);
        Assert.Equal(1, handler.Count); // nunca reintenta un 4xx
    }

    [Fact]
    public async Task CrearLlamada_500_NoReintenta_MarcaTransitorio()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "boom");
        var client = Build(handler);

        var r = await client.CrearLlamadaAsync(Req());

        Assert.False(r.Ok);
        Assert.True(r.Transitorio);
        // create-phone-call NUNCA se reintenta (evita doble llamada), aunque sea 5xx.
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task CrearLlamada_Excepcion_DevuelveErrorTransitorio()
    {
        var client = Build(new ThrowingHandler());
        var r = await client.CrearLlamadaAsync(Req());
        Assert.False(r.Ok);
        Assert.True(r.Transitorio);
    }

    private sealed class StubConfig : IRetellConfig
    {
        public Task EnsureLoadedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public string? ApiKey => "sk_test_key";
        public string? AgentId => "agent_1";
        public string? FromNumber => "+15550001111";
        public string? WebhookToken => "tok";
        public string? TelnyxSipUsername => null;
        public bool EstaConfigurado => true;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;
        public int Count;
        public HttpRequestMessage? Last;
        public StubHandler(HttpStatusCode code, string body) { _code = code; _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Last = request;
            return Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_body) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
    }
}
