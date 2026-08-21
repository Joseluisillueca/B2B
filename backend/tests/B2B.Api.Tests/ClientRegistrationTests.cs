using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Api.Data;
using B2B.Api.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Fase 2: alta de cliente por el agente (prealta) + bandeja de solicitudes. El maestro
// vive en BC (sync de una vía), así que el alta NO crea el cliente en BC: registra la
// solicitud y provisiona el usuario del contacto con su correo de activación. Regla de
// aislamiento: cada agente ve solo SUS solicitudes.
public class ClientRegistrationTests : IClassFixture<ClientRegistrationTests.Factory>
{
    public class Factory : TestWebApplicationFactory { }

    private const string AgentAEmail = "reg-agente-a@test.com";
    private const string AgentBEmail = "reg-agente-b@test.com";
    private const string AgentPassword = "agente-reg-123";
    private const string AgentAId = "REG-AGENT-A";
    private const string AgentBId = "REG-AGENT-B";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ClientRegistrationTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static bool _seeded;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private async Task SeedAsync()
    {
        await Lock.WaitAsync();
        try
        {
            if (_seeded) return;
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = new PasswordHasher<AppUser>();
            foreach (var (email, agentId) in new[] { (AgentAEmail, AgentAId), (AgentBEmail, AgentBId) })
            {
                if (await db.Users.AnyAsync(u => u.Email == email)) continue;
                var user = new AppUser
                {
                    Id = Guid.NewGuid(), Email = email, PasswordHash = "",
                    Role = ClientIdentity.AgentRole, AgentExternalId = agentId, Culture = "es_ES"
                };
                user.PasswordHash = hasher.HashPassword(user, AgentPassword);
                db.Users.Add(user);
            }
            await db.SaveChangesAsync();
            _seeded = true;
        }
        finally { Lock.Release(); }
    }

    private async Task<HttpClient> AgentClientAsync(string email)
    {
        await SeedAsync();
        var token = await _factory.LoginAsync(_client, email, AgentPassword);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    // ── Alta ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Agent_creates_a_client_request_provisions_the_user_and_sends_activation()
    {
        var http = await AgentClientAsync(AgentAEmail);
        var email = "alta-nuevo@cliente.test";

        var response = await http.PostAsJsonAsync("/api/agent/clients", new
        {
            name = "Calzados Nuevos S.L.",
            email,
            web = "https://calzadosnuevos.test",
            createPreClient = true,
            fiscalInfo = new { fiscalName = "Calzados Nuevos S.L.", fiscalId = new { document = "B99999999" } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.GetProperty("preClient").GetBoolean());
        Assert.True(created.GetProperty("activationSent").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // La prealta quedó registrada y asignada al agente A
        var request = await db.ClientRegistrationRequests.SingleAsync(r => r.Email == email);
        Assert.Equal(AgentAId, request.AgentExternalId);
        Assert.Equal("pending", request.Status);
        // El usuario del contacto está provisionado SIN contraseña
        var user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.Equal(ClientIdentity.ClientAdminRole, user.Role);
        Assert.True(string.IsNullOrEmpty(user.PasswordHash));
        // Y se le envió el correo de activación con su token
        Assert.True(await db.SentEmails.AnyAsync(m => m.To == email));
        Assert.True(await db.ActivationTokens.AnyAsync(t => t.UserId == user.Id));
    }

    [Fact]
    public async Task Creating_for_an_existing_user_with_password_does_not_send_activation()
    {
        var http = await AgentClientAsync(AgentAEmail);
        var email = "ya-tiene-clave@cliente.test";

        // El usuario ya existe y tiene contraseña
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new AppUser { Id = Guid.NewGuid(), Email = email, PasswordHash = "", Role = ClientIdentity.ClientAdminRole };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, "clave-existente");
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var response = await http.PostAsJsonAsync("/api/agent/clients", new { name = "Empresa X", email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(created.GetProperty("activationSent").GetBoolean());
    }

    [Theory]
    [InlineData("", "correo@valido.test")]        // nombre vacío
    [InlineData("Empresa", "sin-arroba")]          // email inválido
    public async Task Bad_input_is_rejected(string name, string email)
    {
        var http = await AgentClientAsync(AgentAEmail);
        var response = await http.PostAsJsonAsync("/api/agent/clients", new { name, email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Bandeja + aislamiento ─────────────────────────────────────────────────────

    [Fact]
    public async Task Requests_inbox_shows_only_the_agents_own_requests()
    {
        var httpA = await AgentClientAsync(AgentAEmail);
        var httpB = await AgentClientAsync(AgentBEmail);

        await httpA.PostAsJsonAsync("/api/agent/clients", new { name = "Solo de A", email = "solo-a@cliente.test" });

        var inboxB = await httpB.GetFromJsonAsync<JsonElement>("/api/agent/clients/requests");
        var emailsB = inboxB.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("email").GetString()).ToList();
        Assert.DoesNotContain("solo-a@cliente.test", emailsB);

        var inboxA = await httpA.GetFromJsonAsync<JsonElement>("/api/agent/clients/requests");
        var emailsA = inboxA.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("email").GetString()).ToList();
        Assert.Contains("solo-a@cliente.test", emailsA);
    }

    [Fact]
    public async Task Creating_a_client_requires_the_agent_role()
    {
        // Sin token → 401
        var anon = await _client.PostAsJsonAsync("/api/agent/clients", new { name = "X", email = "x@y.test" });
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        // Token de la cuenta de integración (no agente) → 403
        var token = await _factory.GetConnectorTokenAsync(_client);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var forbidden = await http.PostAsJsonAsync("/api/agent/clients", new { name = "X", email = "x@y.test" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
