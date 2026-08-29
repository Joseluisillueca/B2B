using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using B2B.Api.Data;

namespace B2B.Api.Integration;

// Cliente HTTP hacia las API pages OData de Business Central (contrato 06).
// OAuth2 client credentials contra Entra ID, con token cacheado en memoria.
// Si la conexión no está configurada, lanza BcNotConfigured (el dispatcher lo trata
// como "simulado" — pipeline inerte hasta que se metan las credenciales).
public sealed class BcNotConfigured : Exception
{
    public BcNotConfigured() : base("La conexión con Business Central no está configurada.") { }
}

public sealed record BcResult(bool Ok, int Status, string Body);

public class BcClient(HttpClient http)
{
    private static readonly Dictionary<string, (string Token, DateTime Exp)> TokenCache = new();
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    private async Task<string> TokenAsync(IntegrationSettings s)
    {
        if (!s.BcConfigured) throw new BcNotConfigured();
        var key = s.BcClientId!;
        await TokenLock.WaitAsync();
        try
        {
            if (TokenCache.TryGetValue(key, out var c) && c.Exp > DateTime.UtcNow.AddMinutes(2))
                return c.Token;

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = s.BcClientId!,
                ["client_secret"] = s.BcClientSecret!,
                ["scope"] = string.IsNullOrWhiteSpace(s.BcScope) ? "https://api.businesscentral.dynamics.com/.default" : s.BcScope!,
            });
            using var res = await http.PostAsync(s.BcTokenUrl, form);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Token OAuth de BC falló ({(int)res.StatusCode}): {body}");
            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            TokenCache[key] = (token, DateTime.UtcNow.AddSeconds(expIn));
            return token;
        }
        finally { TokenLock.Release(); }
    }

    private string Url(IntegrationSettings s, string endpoint) =>
        $"{s.BcBaseUrl!.TrimEnd('/')}/{endpoint.TrimStart('/')}";

    public async Task<BcResult> PostAsync(IntegrationSettings s, string endpoint, string json)
    {
        var token = await TokenAsync(s);
        using var req = new HttpRequestMessage(HttpMethod.Post, Url(s, endpoint))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        return new BcResult(res.IsSuccessStatusCode, (int)res.StatusCode, body);
    }

    public async Task<BcResult> GetAsync(IntegrationSettings s, string endpoint)
    {
        var token = await TokenAsync(s);
        using var req = new HttpRequestMessage(HttpMethod.Get, Url(s, endpoint));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        return new BcResult(res.IsSuccessStatusCode, (int)res.StatusCode, body);
    }
}
