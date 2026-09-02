using Visal.Application.Voz;
using Xunit;

namespace Visal.Application.Tests.Voz;

public class RetellWebhookParserTests
{
    [Fact]
    public void Parse_EventoCallEnded_ExtraeCampos()
    {
        var json = """
        {
          "event": "call_ended",
          "call": {
            "call_id": "call_123",
            "call_status": "ended",
            "transcript": "Agent: Hola\nUser: Bien gracias",
            "start_timestamp": 1714608475000,
            "end_timestamp": 1714608535000,
            "disconnection_reason": "user_hangup",
            "call_cost": { "combined_cost": 12.5 }
          }
        }
        """;
        var ev = RetellWebhookParser.Parse(json);
        Assert.NotNull(ev);
        Assert.Equal("call_ended", ev!.Evento);
        Assert.Equal("call_123", ev.CallId);
        Assert.Equal("ended", ev.CallStatus);
        Assert.Equal(60, ev.DuracionSegundos); // (end-start)/1000
        Assert.Equal(12.5m, ev.CostoUsd);
        Assert.Equal("user_hangup", ev.DisconnectionReason);
    }

    [Fact]
    public void Parse_SinCall_DevuelveEventoSinCallId()
    {
        var ev = RetellWebhookParser.Parse("""{ "event": "call_started" }""");
        Assert.NotNull(ev);
        Assert.Equal("call_started", ev!.Evento);
        Assert.Null(ev.CallId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no soy json")]
    [InlineData("{ }")]                 // sin event
    [InlineData("[1,2,3]")]             // no objeto
    public void Parse_Malformado_DevuelveNull(string body)
    {
        Assert.Null(RetellWebhookParser.Parse(body));
    }
}
