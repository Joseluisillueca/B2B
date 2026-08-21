using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Api.Data;
using B2B.Api.Notifications;
using B2B.Api.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Api.Tests;

// Fase 2: activación de cuenta y recuperación de contraseña. La pieza que faltaba —
// el sync y el alta de cliente provisionan usuarios SIN contraseña; este flujo es el
// único camino para que fijen la suya. Reglas duras: token de un solo uso, caduca a
// 72h, y /resend nunca revela si un email existe.
public class ActivationTests : IClassFixture<ActivationTests.Factory>
{
    public class Factory : TestWebApplicationFactory { }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ActivationTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Crea un usuario provisionado (sin contraseña), como lo dejaría el sync
    private async Task<AppUser> NewProvisionedUserAsync(string email, string role = ClientIdentity.ClientAdminRole)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (existing is not null) return existing;

        var user = new AppUser { Id = Guid.NewGuid(), Email = email, PasswordHash = "", Role = role, Culture = "es_ES" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<string> IssueAsync(string email, string purpose = ActivationPurpose.Activation, TimeSpan? lifetime = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activation = scope.ServiceProvider.GetRequiredService<ActivationService>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        return await activation.IssueAsync(user, purpose, lifetime);
    }

    private async Task<HttpResponseMessage> LoginRawAsync(string email, string password) =>
        await _client.PostAsJsonAsync("/api/auth/login", new { email, password, type = "global", longDuration = true });

    // ── Camino feliz ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Activation_link_lets_a_provisioned_user_set_password_and_log_in()
    {
        const string email = "alta1@cliente.test";
        await NewProvisionedUserAsync(email);

        // Antes de activar no puede entrar
        var before = await LoginRawAsync(email, "clave-1234");
        Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);

        var token = await IssueAsync(email);

        var check = await _client.GetFromJsonAsync<JsonElement>($"/api/activation/{token}");
        Assert.True(check.GetProperty("valid").GetBoolean());
        Assert.Equal(email, check.GetProperty("email").GetString());

        var set = await _client.PostAsJsonAsync($"/api/activation/{token}",
            new { password = "clave-1234", repeat = "clave-1234" });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        // Ahora sí puede entrar con la contraseña recién fijada
        var after = await LoginRawAsync(email, "clave-1234");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task A_used_token_cannot_be_reused()
    {
        const string email = "alta2@cliente.test";
        await NewProvisionedUserAsync(email);
        var token = await IssueAsync(email);

        var first = await _client.PostAsJsonAsync($"/api/activation/{token}",
            new { password = "clave-1234", repeat = "clave-1234" });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // El token ya está sellado: ni valida ni se puede canjear otra vez
        var check = await _client.GetFromJsonAsync<JsonElement>($"/api/activation/{token}");
        Assert.False(check.GetProperty("valid").GetBoolean());

        var second = await _client.PostAsJsonAsync($"/api/activation/{token}",
            new { password = "otra-clave-9", repeat = "otra-clave-9" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Issuing_a_new_token_invalidates_the_previous_one()
    {
        const string email = "alta3@cliente.test";
        await NewProvisionedUserAsync(email);
        var first = await IssueAsync(email);
        var second = await IssueAsync(email);

        var checkFirst = await _client.GetFromJsonAsync<JsonElement>($"/api/activation/{first}");
        Assert.False(checkFirst.GetProperty("valid").GetBoolean());
        var checkSecond = await _client.GetFromJsonAsync<JsonElement>($"/api/activation/{second}");
        Assert.True(checkSecond.GetProperty("valid").GetBoolean());
    }

    // ── Validaciones ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("corta", "corta")]              // < 8 caracteres
    [InlineData("clave-1234", "clave-9999")]    // no coincide la repetición
    public async Task Set_password_rejects_short_or_mismatched(string password, string repeat)
    {
        const string email = "alta4@cliente.test";
        await NewProvisionedUserAsync(email);
        var token = await IssueAsync(email);

        var bad = await _client.PostAsJsonAsync($"/api/activation/{token}", new { password, repeat });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // El token sigue vivo: un intento inválido no lo quema
        var check = await _client.GetFromJsonAsync<JsonElement>($"/api/activation/{token}");
        Assert.True(check.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Garbage_token_is_never_valid()
    {
        var check = await _client.GetFromJsonAsync<JsonElement>("/api/activation/no-existe-este-token");
        Assert.False(check.GetProperty("valid").GetBoolean());

        var post = await _client.PostAsJsonAsync("/api/activation/no-existe-este-token",
            new { password = "clave-1234", repeat = "clave-1234" });
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        const string email = "alta5@cliente.test";
        await NewProvisionedUserAsync(email);
        var token = await IssueAsync(email, lifetime: TimeSpan.FromHours(-1));   // ya caducado

        var check = await _client.GetFromJsonAsync<JsonElement>($"/api/activation/{token}");
        Assert.False(check.GetProperty("valid").GetBoolean());
    }

    // ── Reenvío (activación pendiente / olvido de contraseña) ──────────────────────

    [Fact]
    public async Task Resend_sends_activation_and_records_the_email_with_a_link()
    {
        const string email = "alta6@cliente.test";
        await NewProvisionedUserAsync(email);

        var resend = await _client.PostAsJsonAsync("/api/activation/resend", new { email });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sent = await db.SentEmails.SingleOrDefaultAsync(m => m.To == email);
        Assert.NotNull(sent);
        Assert.Equal("log", sent!.Transport);
        Assert.Contains("activate?token=", sent.Body);
        Assert.True(await db.ActivationTokens.AnyAsync(t => t.UserId ==
            db.Users.Single(u => u.Email == email).Id));
    }

    [Fact]
    public async Task Resend_never_reveals_whether_the_email_exists()
    {
        const string ghost = "no-existe@cliente.test";
        var resend = await _client.PostAsJsonAsync("/api/activation/resend", new { email = ghost });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.SentEmails.AnyAsync(m => m.To == ghost));
    }

    [Fact]
    public async Task Resend_for_a_user_with_password_sends_a_reset()
    {
        const string email = "alta7@cliente.test";
        var user = await NewProvisionedUserAsync(email);
        // Le damos contraseña: ahora un reenvío es un restablecimiento, no una activación
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Users.SingleAsync(u => u.Id == user.Id);
            stored.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>()
                .HashPassword(stored, "ya-tengo-clave");
            await db.SaveChangesAsync();
        }

        await _client.PostAsJsonAsync("/api/activation/resend", new { email });

        using var check = _factory.Services.CreateScope();
        var db2 = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var uid = (await db2.Users.SingleAsync(u => u.Email == email)).Id;
        Assert.True(await db2.ActivationTokens.AnyAsync(t => t.UserId == uid && t.Purpose == ActivationPurpose.Reset));
    }
}
