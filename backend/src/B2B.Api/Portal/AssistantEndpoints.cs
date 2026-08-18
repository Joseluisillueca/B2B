using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Portal;

// Asistente del portal: responde en lenguaje natural a preguntas del cliente sobre SU
// actividad ("¿qué artículo he comprado más?", "¿cuánto he comprado de la talla 40?",
// "¿cuánto debo?") para no tener que ir pedido a pedido.
//
// Dos piezas:
//  - GET  /api/portal/purchases   agrega el historial de compras del cliente (por
//                                  artículo y por talla) a partir de sus pedidos.
//  - POST /api/portal/assistant   chat: construye una foto compacta de los datos del
//                                  cliente y responde. Si hay clave de Anthropic
//                                  configurada (Assistant:ApiKey) usa el modelo para
//                                  una respuesta libre; si no, responde de forma
//                                  determinista a partir de la misma foto.
//
// Todo se acota por el clientId del token (PortalScope): nunca ve datos de otros.
public static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/portal/purchases", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var locale = DocumentProjections.Locale(request.Query["locale"]);
            var from = ParseDate(request.Query["from"]);
            var to = ParseDate(request.Query["to"]);
            var summary = await PurchasesAsync(db, principal, locale, from, to);
            return Results.Ok(summary.ToJson());
        }).RequireAuthorization();

        app.MapPost("/api/portal/assistant", async (
            AssistantRequest body, ClaimsPrincipal principal, AppDbContext db,
            IConfiguration config, IHttpClientFactory http) =>
        {
            var question = (body?.Question ?? "").Trim();
            if (question.Length == 0)
                return Results.Json(new { error = "La pregunta está vacía." }, statusCode: 400);
            if (question.Length > 500)
                question = question[..500];

            var locale = DocumentProjections.Locale(null);
            var snapshot = await SnapshotAsync(db, principal, locale);

            var apiKey = config["Assistant:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var llm = await AskModelAsync(http, config, apiKey!, question, snapshot, body?.History ?? []);
                if (llm is not null)
                    return Results.Ok(new { answer = llm, source = "model" });
                // Si el modelo falla (red, cuota…), no dejamos al usuario sin respuesta
            }

            return Results.Ok(new { answer = DeterministicAnswer(question, snapshot), source = "rules" });
        }).RequireAuthorization();
    }

    // ══════════════ Agregación de compras ══════════════

    public sealed record ProductLine(string Name, string Reference, decimal Units, decimal Amount);
    public sealed record SizeLine(string Size, decimal Units);
    public sealed record PurchaseSummary(
        int OrderCount, decimal TotalUnits, decimal TotalAmount,
        IReadOnlyList<ProductLine> TopProducts, IReadOnlyList<SizeLine> BySize)
    {
        public object ToJson() => new
        {
            orderCount = OrderCount,
            totalUnits = TotalUnits,
            totalAmount = TotalAmount,
            topProducts = TopProducts.Select(p => new { name = p.Name, reference = p.Reference, units = p.Units, amount = p.Amount }),
            bySize = BySize.Select(s => new { size = s.Size, units = s.Units })
        };
    }

    public static async Task<PurchaseSummary> PurchasesAsync(
        AppDbContext db, ClaimsPrincipal principal, string locale,
        DateTimeOffset? from, DateTimeOffset? to)
    {
        var docs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.OrderEntity);

        var byProduct = new Dictionary<string, (string Name, string Ref, decimal Units, decimal Amount)>(StringComparer.OrdinalIgnoreCase);
        var bySize = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal totalUnits = 0, totalAmount = 0;
        var orders = 0;

        foreach (var (id, payload) in docs)
        {
            // Order() devuelve null para las devoluciones (type NOT_DEFINED / importe
            // negativo): no cuentan como compra.
            var order = DocumentProjections.Order(id, payload);
            if (order is null) continue;
            if (from is not null && order.Date is { } d1 && d1 < from) continue;
            if (to is not null && order.Date is { } d2 && d2 > to) continue;
            orders++;

            foreach (var line in DocumentProjections.OrderLines(payload, locale))
            {
                if (line.Quantity <= 0) continue;
                // El artículo que el cliente reconoce es el nombre del modelo+color
                var key = line.Name.Length > 0 ? line.Name : line.Reference;
                if (key.Length == 0) continue;
                if (!byProduct.TryGetValue(key, out var prev))
                    prev = (line.Name, line.Reference, 0m, 0m);
                byProduct[key] = (prev.Name, prev.Ref, prev.Units + line.Quantity, prev.Amount + line.Amount);

                if (line.Size.Length > 0)
                    bySize[line.Size] = (bySize.TryGetValue(line.Size, out var u) ? u : 0m) + line.Quantity;

                totalUnits += line.Quantity;
                totalAmount += line.Amount;
            }
        }

        var topProducts = byProduct.Values
            .OrderByDescending(p => p.Units).ThenByDescending(p => p.Amount)
            .Select(p => new ProductLine(p.Name, p.Ref, p.Units, p.Amount))
            .ToList();
        var sizes = bySize
            .OrderByDescending(s => s.Value)
            .Select(s => new SizeLine(s.Key, s.Value))
            .ToList();

        return new PurchaseSummary(orders, totalUnits, totalAmount, topProducts, sizes);
    }

    // ══════════════ Foto de datos para el asistente ══════════════

    public sealed record Snapshot(
        string ClientName, PurchaseSummary Purchases,
        decimal Billed12m, int InvoiceCount, decimal Debt, int Overdue,
        int OrdersOpen, int OrdersTotal);

    private static async Task<Snapshot> SnapshotAsync(AppDbContext db, ClaimsPrincipal principal, string locale)
    {
        var purchases = await PurchasesAsync(db, principal, locale, null, null);

        var actor = await PortalScope.ActorAsync(principal, db);
        var clientPayload = await PortalScope.ClientPayloadAsync(db, actor?.ClientId);
        var clientName = DocumentProjections.Text(clientPayload?["name"]);

        // Facturas (deuda, vencidas) y facturado de los últimos 12 meses
        var invoiceDocs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.InvoiceEntity);
        var today = DateTimeOffset.UtcNow;
        var todayDate = DateOnly.FromDateTime(today.UtcDateTime);
        decimal debt = 0, billed = 0;
        int overdue = 0, invoiceCount = 0;
        var yearAgo = today.AddMonths(-12);
        foreach (var (id, payload) in invoiceDocs)
        {
            var inv = DocumentProjections.Invoice(id, payload, locale, todayDate);
            invoiceCount++;
            debt += inv.Debt;
            if (inv.Status == "overdue") overdue++;
            if (inv.Date is { } dt && dt >= yearAgo) billed += inv.Total;
        }

        var orderDocs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.OrderEntity);
        int ordersTotal = 0, ordersOpen = 0;
        foreach (var (id, payload) in orderDocs)
        {
            var o = DocumentProjections.Order(id, payload);
            if (o is null) continue;
            ordersTotal++;
            if (o.Status == "open") ordersOpen++;
        }

        return new Snapshot(clientName, purchases, billed, invoiceCount, debt, overdue, ordersOpen, ordersTotal);
    }

    // ══════════════ Respuesta determinista (sin modelo) ══════════════

    private static string DeterministicAnswer(string question, Snapshot s)
    {
        var q = Normalize(question);
        var p = s.Purchases;

        // "¿cuánto he comprado de la talla 40?"
        if (q.Contains("talla"))
        {
            var size = ExtractSize(question);
            if (size is not null)
            {
                var match = p.BySize.FirstOrDefault(x => Normalize(x.Size) == Normalize(size));
                if (match is not null)
                    return $"Has comprado **{Units(match.Units)}** de la talla {match.Size} en total.";
                return $"No encuentro compras de la talla {size} en tu historial.";
            }
            if (p.BySize.Count > 0)
                return "Unidades por talla (de más a menos):\n" +
                    string.Join("\n", p.BySize.Take(12).Select(x => $"- Talla {x.Size}: {Units(x.Units)}"));
        }

        // "¿qué artículo he comprado más?"
        if ((q.Contains("mas") || q.Contains("top") || q.Contains("mejor")) &&
            (q.Contains("articul") || q.Contains("product") || q.Contains("model") || q.Contains("comprad") || q.Contains("vendid")))
        {
            if (p.TopProducts.Count == 0) return "Todavía no tengo pedidos tuyos para calcularlo.";
            var top = p.TopProducts.Take(5).ToList();
            return "Tus artículos más comprados:\n" +
                string.Join("\n", top.Select((x, i) => $"{i + 1}. {x.Name} — {Units(x.Units)} ({Eur(x.Amount)})"));
        }

        // Deuda / pendiente
        if (q.Contains("deb") || q.Contains("deuda") || q.Contains("pendiente") || q.Contains("vencid"))
        {
            var v = s.Overdue > 0 ? $" · {s.Overdue} factura(s) vencida(s)" : "";
            return $"Tienes **{Eur(s.Debt)}** pendientes de cobro{v}.";
        }

        // Ventas / compras / facturado
        if (q.Contains("vendid") || q.Contains("comprad") || q.Contains("facturad") || q.Contains("gastad") || q.Contains("cuanto"))
            return $"En los últimos 12 meses has facturado **{Eur(s.Billed12m)}** en {s.InvoiceCount} factura(s). " +
                   $"Total de tu historial de pedidos: {Units(p.TotalUnits)} en {p.OrderCount} pedido(s) ({Eur(p.TotalAmount)}).";

        // Pedidos
        if (q.Contains("pedido"))
            return $"Tienes **{s.OrdersTotal}** pedidos, {s.OrdersOpen} de ellos abiertos.";

        // Resumen general por defecto
        var topName = p.TopProducts.Count > 0 ? p.TopProducts[0].Name : "—";
        return "Esto es lo que sé de tu actividad:\n" +
               $"- Pedidos: {s.OrdersTotal} ({s.OrdersOpen} abiertos)\n" +
               $"- Facturado (12 meses): {Eur(s.Billed12m)} · pendiente de cobro: {Eur(s.Debt)}\n" +
               $"- Artículo más comprado: {topName}\n" +
               "Puedes preguntarme por un artículo, una talla, tus ventas o tu deuda.";
    }

    // ══════════════ Respuesta con el modelo (Anthropic) ══════════════

    private static async Task<string?> AskModelAsync(
        IHttpClientFactory http, IConfiguration config, string apiKey,
        string question, Snapshot s, IReadOnlyList<AssistantTurn> history)
    {
        try
        {
            var model = config["Assistant:Model"] ?? "claude-haiku-4-5-20251001";
            var context = BuildContext(s);
            var system =
                "Eres el asistente del portal B2B de lejan. Respondes en español, de forma breve y concreta, " +
                "SOLO con los datos del cliente que se te dan a continuación. Si la respuesta no está en los datos, " +
                "dilo con naturalidad. Importes en euros (formato español) y no inventes cifras.\n\n" +
                "DATOS DEL CLIENTE (JSON):\n" + context;

            var messages = new JsonArray();
            foreach (var turn in history.TakeLast(6))
                messages.Add(new JsonObject { ["role"] = turn.Role == "assistant" ? "assistant" : "user", ["content"] = turn.Content ?? "" });
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });

            var payload = new JsonObject
            {
                ["model"] = model,
                ["max_tokens"] = 600,
                ["system"] = system,
                ["messages"] = messages
            };

            var client = http.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            req.Content.Headers.ContentType!.CharSet = null;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var res = await client.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode) return null;

            var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token));
            var text = (json?["content"] as JsonArray)?
                .FirstOrDefault(b => DocumentProjections.Text(b?["type"]) == "text");
            var answer = DocumentProjections.Text(text?["text"]);
            return answer.Length > 0 ? answer : null;
        }
        catch (Exception)
        {
            return null;   // cualquier fallo → el llamador usa la respuesta determinista
        }
    }

    private static string BuildContext(Snapshot s) => new JsonObject
    {
        ["cliente"] = s.ClientName,
        ["pedidos"] = new JsonObject { ["total"] = s.OrdersTotal, ["abiertos"] = s.OrdersOpen },
        ["facturado12m"] = s.Billed12m,
        ["facturas"] = s.InvoiceCount,
        ["deudaPendiente"] = s.Debt,
        ["facturasVencidas"] = s.Overdue,
        ["historialUnidades"] = s.Purchases.TotalUnits,
        ["historialImporte"] = s.Purchases.TotalAmount,
        ["articulosMasComprados"] = new JsonArray(s.Purchases.TopProducts.Take(15)
            .Select(p => (JsonNode)new JsonObject { ["articulo"] = p.Name, ["unidades"] = p.Units, ["importe"] = p.Amount }).ToArray()),
        ["unidadesPorTalla"] = new JsonArray(s.Purchases.BySize.Take(30)
            .Select(x => (JsonNode)new JsonObject { ["talla"] = x.Size, ["unidades"] = x.Units }).ToArray())
    }.ToJsonString();

    // ══════════════ Utilidades ══════════════

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static string Eur(decimal v) => v.ToString("N2", Es) + " €";
    private static string Units(decimal v) => v == Math.Truncate(v)
        ? ((long)v).ToString("N0", Es) + " uds." : v.ToString("N2", Es) + " uds.";

    private static string Normalize(string text)
    {
        var lower = text.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString();
    }

    // Extrae un token de talla de la pregunta ("talla 40", "de la 36", "talla U")
    private static string? ExtractSize(string question)
    {
        var words = question.Split([' ', ',', '.', '?', '¿', ':', ';'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (!words[i].Equals("talla", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 < words.Length) return words[i + 1].ToUpperInvariant();
        }
        // "de la 40"
        foreach (var w in words)
            if (w.Length is >= 1 and <= 3 && w.All(char.IsDigit)) return w;
        return null;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    public sealed record AssistantTurn(string? Role, string? Content);
    public sealed record AssistantRequest(string? Question, IReadOnlyList<AssistantTurn>? History);
}
