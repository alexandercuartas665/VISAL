using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Visal.Application.Auth;
using Visal.Application.Tenancy;
using Visal.Domain.Enums;

namespace Visal.Integration.Tests.Auth;

public sealed class WhatsAppLineTests : IClassFixture<VisalApiFactory>
{
    private readonly VisalApiFactory _factory;

    public WhatsAppLineTests(VisalApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TenantAdmin_CanCreateConnectAndAssignLine()
    {
        var client = await TenantAdminClientForBAsync();

        // Crear linea
        var create = await client.PostAsJsonAsync("/tenant/whatsapp-lines",
            new CreateWhatsAppLineRequest($"linea-{Guid.NewGuid():N}", "+573001112233"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var line = (await create.Content.ReadFromJsonAsync<WhatsAppLineDto>())!;
        Assert.Equal(WhatsAppLineStatus.Created, line.Status);

        // Conectar (simula callback Evolution)
        var connect = await client.PostAsJsonAsync($"/tenant/whatsapp-lines/{line.Id}/status",
            new ChangeLineStatusRequest(WhatsAppLineStatus.Connected));
        connect.EnsureSuccessStatusCode();
        var connected = (await connect.Content.ReadFromJsonAsync<WhatsAppLineDto>())!;
        Assert.Equal(WhatsAppLineStatus.Connected, connected.Status);
        Assert.NotNull(connected.LastConnectedAt);

        // Asignar a un usuario del tenant B (multi@ es TenantUser en B)
        var users = await client.GetFromJsonAsync<List<Visal.Application.Tenancy.TenantUserDto>>("/tenant/users");
        var someUser = users!.First();
        var assign = await client.PostAsJsonAsync($"/tenant/whatsapp-lines/{line.Id}/assignment",
            new AssignLineRequest(someUser.Id));
        assign.EnsureSuccessStatusCode();
        var assigned = (await assign.Content.ReadFromJsonAsync<WhatsAppLineDto>())!;
        Assert.Equal(someUser.Id, assigned.AssignedToTenantUserId);
    }

    [Fact]
    public async Task Advisor_CannotManageLines_Returns403()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(VisalApiFactory.SingleEmail, VisalApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/tenant/whatsapp-lines");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> TenantAdminClientForBAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/connect/token",
            new LoginRequest(VisalApiFactory.MultiEmail, VisalApiFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var switchResponse = await client.PostAsJsonAsync("/connect/switch-tenant", new SwitchTenantRequest(_factory.TenantBId));
        var switched = (await switchResponse.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switched.AccessToken);
        return client;
    }
}
