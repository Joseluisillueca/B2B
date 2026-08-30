using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using B2B.Api.Data;

namespace B2B.Api.Portal;

// Asistente del portal: responde en lenguaje natural a preguntas del cliente sobre SU
// actividad ("¿qué artículo he comprado más?", "¿cuánto he comprado de la talla 40?",
// "¿cuál fue mi mejor mes?", "¿qué pedí en julio?", "¿cuánto debo?") para no tener que
// ir pedido a pedido.
//
//  - GET  /api/portal/purchases   agrega el historial de compras del cliente (por
//                                 artículo, por talla y por artículo+talla).
//  - POST /api/portal/assistant   chat: construye una foto de los datos del cliente y
//                                 responde. Con clave de Anthropic (Assistant:ApiKey)
//                                 usa el modelo para preguntas libres; sin clave,
//                                 responde de forma determinista a un amplio catálogo
//                                 de preguntas frecuentes.
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
            if (question.Length > 500) question = question[..500];

            var snapshot = await SnapshotAsync(db, principal, DocumentProjections.Locale(null));

            var apiKey = config["Assistant:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var llm = await AskModelAsync(http, config, apiKey!, question, snapshot, body?.History ?? []);
                if (llm is not null)
                    return Results.Ok(new { answer = llm, source = "model" });
            }

            return Results.Ok(new { answer = DeterministicAnswer(question, snapshot), source = "rules" });
        }).RequireAuthorization();
    }

    // ══════════════ Agregación de compras ══════════════

    public sealed record ProductLine(string Name, string Reference, decimal Units, decimal Amount);
    public sealed record SizeLine(string Size, decimal Units);
    public sealed record ProductSize(string Name, string Size, decimal Units);
    public sealed record PurchaseSummary(
        int OrderCount, decimal TotalUnits, decimal TotalAmount,
        IReadOnlyList<ProductLine> TopProducts, IReadOnlyList<SizeLine> BySize,
        IReadOnlyList<ProductSize> ByProductSize)
    {
        public object ToJson() => new
        {
            orderCount = OrderCount,
            totalUnits = TotalUnits,
            totalAmount = TotalAmount,
            topProducts = TopProducts.Select(p => new { name = p.Name, reference = p.Reference, units = p.Units, amount = p.Amount }),
            bySize = BySize.Select(s => new { size = s.Size, units = s.Units }),
            byProductSize = ByProductSize.Select(x => new { name = x.Name, size = x.Size, units = x.Units })
        };
    }

    public static async Task<PurchaseSummary> PurchasesAsync(
        AppDbContext db, ClaimsPrincipal principal, string locale, DateTimeOffset? from, DateTimeOffset? to)
    {
        var docs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.OrderEntity);

        var byProduct = new Dictionary<string, (string Name, string Ref, decimal Units, decimal Amount)>(StringComparer.OrdinalIgnoreCase);
        var bySize = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var byProductSize = new Dictionary<(string, string), decimal>();
        decimal totalUnits = 0, totalAmount = 0;
        var orders = 0;

        foreach (var (id, payload) in docs)
        {
            var order = DocumentProjections.Order(id, payload);   // null = devolución, no cuenta
            if (order is null) continue;
            if (from is not null && order.Date is { } d1 && d1 < from) continue;
            if (to is not null && order.Date is { } d2 && d2 > to) continue;
            orders++;

            foreach (var line in DocumentProjections.OrderLines(payload, locale))
            {
                if (line.Quantity <= 0) continue;
                var key = line.Name.Length > 0 ? line.Name : line.Reference;
                if (key.Length == 0) continue;

                if (!byProduct.TryGetValue(key, out var prev)) prev = (line.Name, line.Reference, 0m, 0m);
                byProduct[key] = (prev.Name, prev.Ref, prev.Units + line.Quantity, prev.Amount + line.Amount);

                if (line.Size.Length > 0)
                {
                    bySize[line.Size] = (bySize.TryGetValue(line.Size, out var u) ? u : 0m) + line.Quantity;
                    var ps = (key, line.Size);
                    byProductSize[ps] = (byProductSize.TryGetValue(ps, out var pu) ? pu : 0m) + line.Quantity;
                }

                totalUnits += line.Quantity;
                totalAmount += line.Amount;
            }
        }

        var topProducts = byProduct.Values
            .OrderByDescending(p => p.Units).ThenByDescending(p => p.Amount)
            .Select(p => new ProductLine(p.Name, p.Ref, p.Units, p.Amount)).ToList();
        var sizes = bySize.OrderByDescending(s => s.Value).Select(s => new SizeLine(s.Key, s.Value)).ToList();
        var productSizes = byProductSize.OrderByDescending(x => x.Value)
            .Select(x => new ProductSize(x.Key.Item1, x.Key.Item2, x.Value)).ToList();

        return new PurchaseSummary(orders, totalUnits, totalAmount, topProducts, sizes, productSizes);
    }

    // ══════════════ Foto de datos para el asistente ══════════════

    public sealed record OrderBrief(string Number, DateTimeOffset? Date, string Type, decimal Units, decimal Total, string Status);
    public sealed record InvoiceBrief(string Number, DateTimeOffset? Date, decimal Total, decimal Debt, string Status, DateTimeOffset? DueDate);
    public sealed record MonthSale(string Month, decimal Amount, int Count);

    public sealed record Snapshot(
        string ClientName, decimal? CreditLimit,
        PurchaseSummary Purchases,
        IReadOnlyList<OrderBrief> Orders,
        IReadOnlyList<InvoiceBrief> Invoices,
        IReadOnlyList<MonthSale> MonthlySales,
        decimal Billed12m, int InvoiceCount, decimal Debt, int Overdue,
        int OrdersOpen, int OrdersTotal, IReadOnlyDictionary<string, int> OrderStatusCounts);

    private static async Task<Snapshot> SnapshotAsync(AppDbContext db, ClaimsPrincipal principal, string locale)
    {
        var purchases = await PurchasesAsync(db, principal, locale, null, null);

        var actor = await PortalScope.ActorAsync(principal, db);
        var clientPayload = await PortalScope.ClientPayloadAsync(db, actor?.ClientId);
        var clientName = DocumentProjections.Text(clientPayload?["name"]);
        decimal? creditLimit = null;
        if (clientPayload?["creditInfo"]?["value"] is { } cv && decimal.TryParse(
                cv.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lim)) creditLimit = lim;

        var today = DateTimeOffset.UtcNow;
        var todayDate = DateOnly.FromDateTime(today.UtcDateTime);
        var yearAgo = today.AddMonths(-12);

        var invoiceDocs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.InvoiceEntity);
        var invoices = new List<InvoiceBrief>();
        var monthly = new Dictionary<string, (decimal Amount, int Count)>();
        decimal debt = 0, billed = 0;
        int overdue = 0;
        foreach (var (id, payload) in invoiceDocs)
        {
            var inv = DocumentProjections.Invoice(id, payload, locale, todayDate);
            invoices.Add(new InvoiceBrief(inv.Number, inv.Date, inv.Total, inv.Debt, inv.Status, inv.DueDate));
            debt += inv.Debt;
            if (inv.Status == "overdue") overdue++;
            if (inv.Date is { } dt)
            {
                if (dt >= yearAgo) billed += inv.Total;
                var m = dt.ToString("yyyy-MM");
                monthly.TryGetValue(m, out var cur);   // (0,0) si no existe
                monthly[m] = (cur.Amount + inv.Total, cur.Count + 1);
            }
        }

        var orderDocs = await PortalScope.DocumentsAsync(db, principal, DocumentProjections.OrderEntity);
        var orders = new List<OrderBrief>();
        var statusCounts = new Dictionary<string, int>();
        foreach (var (id, payload) in orderDocs)
        {
            var o = DocumentProjections.Order(id, payload);
            if (o is null) continue;
            orders.Add(new OrderBrief(o.Number, o.Date, o.Type, o.Units, o.Total, o.Status));
            statusCounts[o.Status] = (statusCounts.TryGetValue(o.Status, out var c) ? c : 0) + 1;
        }

        var monthlySales = monthly.OrderBy(m => m.Key)
            .Select(m => new MonthSale(m.Key, m.Value.Amount, m.Value.Count)).ToList();

        return new Snapshot(clientName, creditLimit, purchases,
            orders.OrderByDescending(o => o.Date).ToList(),
            invoices.OrderByDescending(i => i.Date).ToList(), monthlySales,
            billed, invoices.Count, debt, overdue,
            statusCounts.TryGetValue("open", out var op) ? op : 0, orders.Count, statusCounts);
    }

    // ══════════════ Respuesta determinista (sin modelo) ══════════════

    private static string DeterministicAnswer(string question, Snapshot s)
    {
        var q = Normalize(question);
        var p = s.Purchases;

        // Ayuda / saludo
        if (q.Length < 6 && (q.Contains("hola") || q.Contains("ey") || q.Contains("hey")) || q.Contains("ayuda")
            || q.Contains("que puedes") || q.Contains("que sabes") || q.Contains("como funciona"))
            return Help();

        // Talla, opcionalmente de un artículo concreto
        if (q.Contains("talla"))
        {
            var size = ExtractSize(question);
            var product = MatchProduct(question, s);
            if (size is not null && product is not null)
            {
                var m = p.ByProductSize.FirstOrDefault(x =>
                    Normalize(x.Name) == Normalize(product) && Normalize(x.Size) == Normalize(size));
                return m is not null
                    ? $"Del artículo **{product}** has comprado **{Units(m.Units)}** de la talla {m.Size}."
                    : $"No encuentro compras de la talla {size} del artículo «{product}».";
            }
            if (size is not null)
            {
                var m = p.BySize.FirstOrDefault(x => Normalize(x.Size) == Normalize(size));
                return m is not null
                    ? $"Has comprado **{Units(m.Units)}** de la talla {m.Size} en total."
                    : $"No encuentro compras de la talla {size} en tu historial.";
            }
            if (p.BySize.Count > 0)
                return "Unidades por talla (de más a menos):\n" +
                    string.Join("\n", p.BySize.Take(12).Select(x => $"- Talla {x.Size}: {Units(x.Units)}"));
        }

        // "¿cuánto he comprado del artículo X?" (sin talla)
        var namedProduct = MatchProduct(question, s);
        if (namedProduct is not null && (q.Contains("comprad") || q.Contains("pedid") || q.Contains("cuant") || q.Contains("unidad")))
        {
            var prod = p.TopProducts.FirstOrDefault(x => Normalize(x.Name) == Normalize(namedProduct));
            if (prod is not null)
                return $"Del artículo **{prod.Name}** has comprado **{Units(prod.Units)}** por {Eur(prod.Amount)} en total.";
        }

        // Artículo más comprado
        if ((q.Contains("mas") || q.Contains("top") || q.Contains("mejor")) &&
            (q.Contains("articul") || q.Contains("product") || q.Contains("model") || q.Contains("comprad") || q.Contains("vendid")))
        {
            if (p.TopProducts.Count == 0) return "Todavía no tengo pedidos tuyos para calcularlo.";
            return "Tus artículos más comprados:\n" + string.Join("\n",
                p.TopProducts.Take(5).Select((x, i) => $"{i + 1}. {x.Name} — {Units(x.Units)} ({Eur(x.Amount)})"));
        }

        // Mejor mes (de ventas facturadas)
        if (q.Contains("mejor mes") || (q.Contains("mes") && (q.Contains("mas vend") || q.Contains("mas factur") || q.Contains("que mes"))))
        {
            if (s.MonthlySales.Count == 0) return "Todavía no tengo facturas para saber tu mejor mes.";
            var best = s.MonthlySales.OrderByDescending(m => m.Amount).First();
            return $"Tu mejor mes fue **{MonthName(best.Month)}**, con {Eur(best.Amount)} facturados en {best.Count} factura(s).";
        }

        // Pedidos de un mes concreto ("¿qué pedí en julio?")
        var month = ExtractMonth(q);
        if (month is not null && q.Contains("pedi"))
        {
            var list = s.Orders.Where(o => o.Date is { } d && d.Month == month.Value).OrderByDescending(o => o.Date).ToList();
            if (list.Count == 0) return $"No hiciste pedidos en {MonthNameNum(month.Value)}.";
            return $"Pedidos de {MonthNameNum(month.Value)}:\n" + string.Join("\n",
                list.Take(15).Select(o => $"- {o.Number} · {Fecha(o.Date)} · {Units(o.Units)} · {Eur(o.Total)} · {OrderStatus(o.Status)}"));
        }

        // Ventas de un mes concreto
        if (month is not null && (q.Contains("vend") || q.Contains("factur")))
        {
            var m = s.MonthlySales.FirstOrDefault(x => MonthNum(x.Month) == month.Value);
            return m is not null
                ? $"En {MonthName(m.Month)} facturaste {Eur(m.Amount)} en {m.Count} factura(s)."
                : $"No tengo ventas facturadas en {MonthNameNum(month.Value)}.";
        }

        // Pedidos por estado / totales
        if (q.Contains("pedido"))
        {
            if (q.Contains("abiert")) return $"Tienes **{Count(s.OrderStatusCounts, "open")}** pedidos abiertos.";
            if (q.Contains("enviad") && q.Contains("parcial")) return $"Tienes **{Count(s.OrderStatusCounts, "partially-shipped")}** pedidos en envío parcial.";
            if (q.Contains("enviad")) return $"Tienes **{Count(s.OrderStatusCounts, "shipped")}** pedidos enviados.";
            if (q.Contains("factur")) return $"Tienes **{Count(s.OrderStatusCounts, "invoiced")}** pedidos facturados.";
            if (q.Contains("cancel")) return $"Tienes **{Count(s.OrderStatusCounts, "canceled")}** pedidos cancelados.";
            if (q.Contains("ultimo") || q.Contains("reciente"))
            {
                var last = s.Orders.FirstOrDefault();
                return last is not null
                    ? $"Tu último pedido es **{last.Number}** del {Fecha(last.Date)}: {Units(last.Units)} por {Eur(last.Total)} ({OrderStatus(last.Status)})."
                    : "Todavía no tienes pedidos.";
            }
            return $"Tienes **{s.OrdersTotal}** pedidos en total, {s.OrdersOpen} de ellos abiertos.";
        }

        // Facturas / deuda
        if (q.Contains("vencid"))
        {
            var v = s.Invoices.Where(i => i.Status == "overdue").ToList();
            if (v.Count == 0) return "No tienes facturas vencidas. 👍";
            return $"Tienes **{v.Count}** factura(s) vencida(s):\n" + string.Join("\n",
                v.Take(10).Select(i => $"- {i.Number} · {Eur(i.Debt)} · venció el {Fecha(i.DueDate)}"));
        }
        if (q.Contains("ultima") && q.Contains("factur"))
        {
            var last = s.Invoices.FirstOrDefault();
            return last is not null
                ? $"Tu última factura es **{last.Number}** del {Fecha(last.Date)}: {Eur(last.Total)} ({InvoiceStatus(last.Status)})."
                : "Todavía no tienes facturas.";
        }
        if (q.Contains("deb") || q.Contains("deuda") || q.Contains("pendiente") || q.Contains("pagar"))
        {
            var v = s.Overdue > 0 ? $" · {s.Overdue} factura(s) vencida(s)" : "";
            return $"Tienes **{Eur(s.Debt)}** pendientes de cobro{v}.";
        }

        // Crédito
        if (q.Contains("credito") || q.Contains("limite"))
        {
            if (s.CreditLimit is not { } lim) return "No tengo configurado tu límite de crédito.";
            return $"Tu límite de crédito es **{Eur(lim)}**. Ahora mismo tienes {Eur(s.Debt)} pendientes de cobro" +
                   $" (te quedan {Eur(lim - s.Debt)}).";
        }

        // Promedio por pedido
        if (q.Contains("promedio") || q.Contains("media"))
        {
            if (p.OrderCount == 0) return "Todavía no tengo pedidos tuyos.";
            return $"Tu pedido medio es de {Eur(p.TotalAmount / p.OrderCount)} y {Units(p.TotalUnits / p.OrderCount)}.";
        }

        // Unidades / gasto total
        if ((q.Contains("unidad") || q.Contains("cuant")) && (q.Contains("total") || q.Contains("comprad")))
            return $"En total has comprado **{Units(p.TotalUnits)}** en {p.OrderCount} pedido(s), por {Eur(p.TotalAmount)}.";

        // Ventas / facturado (12 meses)
        if (q.Contains("vend") || q.Contains("comprad") || q.Contains("factur") || q.Contains("gastad") || q.Contains("cuanto"))
            return $"En los últimos 12 meses has facturado **{Eur(s.Billed12m)}** en {s.InvoiceCount} factura(s). " +
                   $"Tu historial de pedidos suma {Units(p.TotalUnits)} en {p.OrderCount} pedido(s) ({Eur(p.TotalAmount)}).";

        return Help();
    }

    private static string Help() =>
        "Puedo responderte, por ejemplo:\n" +
        "- ¿Qué artículo he comprado más?\n" +
        "- ¿Cuánto he comprado de la talla 40? (o de un artículo concreto)\n" +
        "- ¿Cuál fue mi mejor mes? · ¿Qué pedí en julio?\n" +
        "- ¿Cuántos pedidos tengo abiertos? · ¿Cuál es mi último pedido?\n" +
        "- ¿Cuánto debo? · ¿Tengo facturas vencidas? · ¿Cuánto me queda de crédito?";

    // ══════════════ Respuesta con el modelo (Anthropic) ══════════════

    private static async Task<string?> AskModelAsync(
        IHttpClientFactory http, IConfiguration config, string apiKey,
        string question, Snapshot s, IReadOnlyList<AssistantTurn> history)
    {
        try
        {
            var model = config["Assistant:Model"] ?? "claude-haiku-4-5-20251001";
            var system =
                "Eres el asistente del portal B2B de Mito Projects. Respondes en español, breve y concreto, SOLO con los " +
                "datos del cliente que se te dan. Si la respuesta no está en los datos, dilo. Importes en euros " +
                "(formato español); no inventes cifras.\n\nDATOS DEL CLIENTE (JSON):\n" + BuildContext(s);

            var messages = new JsonArray();
            foreach (var turn in history.TakeLast(6))
                messages.Add(new JsonObject { ["role"] = turn.Role == "assistant" ? "assistant" : "user", ["content"] = turn.Content ?? "" });
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });

            var payload = new JsonObject
            {
                ["model"] = model, ["max_tokens"] = 700, ["system"] = system, ["messages"] = messages
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
            var text = (json?["content"] as JsonArray)?.FirstOrDefault(b => DocumentProjections.Text(b?["type"]) == "text");
            var answer = DocumentProjections.Text(text?["text"]);
            return answer.Length > 0 ? answer : null;
        }
        catch (Exception) { return null; }
    }

    private static string BuildContext(Snapshot s) => new JsonObject
    {
        ["cliente"] = s.ClientName,
        ["limiteCredito"] = s.CreditLimit,
        ["pedidos"] = new JsonObject { ["total"] = s.OrdersTotal, ["abiertos"] = s.OrdersOpen },
        ["pedidosPorEstado"] = new JsonObject(s.OrderStatusCounts.Select(kv =>
            new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value))),
        ["facturado12m"] = s.Billed12m,
        ["deudaPendiente"] = s.Debt,
        ["facturasVencidas"] = s.Overdue,
        ["ventasPorMes"] = new JsonArray(s.MonthlySales.Select(m =>
            (JsonNode)new JsonObject { ["mes"] = m.Month, ["importe"] = m.Amount, ["facturas"] = m.Count }).ToArray()),
        ["historialUnidades"] = s.Purchases.TotalUnits,
        ["historialImporte"] = s.Purchases.TotalAmount,
        ["articulosMasComprados"] = new JsonArray(s.Purchases.TopProducts.Take(20).Select(p =>
            (JsonNode)new JsonObject { ["articulo"] = p.Name, ["unidades"] = p.Units, ["importe"] = p.Amount }).ToArray()),
        ["unidadesPorTalla"] = new JsonArray(s.Purchases.BySize.Take(40).Select(x =>
            (JsonNode)new JsonObject { ["talla"] = x.Size, ["unidades"] = x.Units }).ToArray()),
        ["unidadesPorArticuloYTalla"] = new JsonArray(s.Purchases.ByProductSize.Take(120).Select(x =>
            (JsonNode)new JsonObject { ["articulo"] = x.Name, ["talla"] = x.Size, ["unidades"] = x.Units }).ToArray()),
        ["pedidosRecientes"] = new JsonArray(s.Orders.Take(20).Select(o =>
            (JsonNode)new JsonObject { ["numero"] = o.Number, ["fecha"] = o.Date?.ToString("yyyy-MM-dd"),
                ["unidades"] = o.Units, ["importe"] = o.Total, ["estado"] = o.Status }).ToArray()),
        ["facturasRecientes"] = new JsonArray(s.Invoices.Take(20).Select(i =>
            (JsonNode)new JsonObject { ["numero"] = i.Number, ["fecha"] = i.Date?.ToString("yyyy-MM-dd"),
                ["importe"] = i.Total, ["deuda"] = i.Debt, ["estado"] = i.Status,
                ["vence"] = i.DueDate?.ToString("yyyy-MM-dd") }).ToArray())
    }.ToJsonString();

    // ══════════════ Utilidades ══════════════

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static string Eur(decimal v) => v.ToString("N2", Es) + " €";
    private static string Units(decimal v) => v == Math.Truncate(v)
        ? ((long)v).ToString("N0", Es) + " uds." : v.ToString("N2", Es) + " uds.";
    private static string Fecha(DateTimeOffset? d) => d?.ToString("dd/MM/yyyy") ?? "—";
    private static int Count(IReadOnlyDictionary<string, int> d, string k) => d.TryGetValue(k, out var v) ? v : 0;

    private static readonly string[] Months =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio",
         "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    private static int? ExtractMonth(string normalized)
    {
        for (var i = 0; i < Months.Length; i++)
            if (normalized.Contains(Months[i])) return i + 1;
        if (normalized.Contains("setiembre")) return 9;
        return null;
    }

    private static string MonthNameNum(int m) => m is >= 1 and <= 12
        ? char.ToUpper(Months[m - 1][0]) + Months[m - 1][1..] : m.ToString();
    private static int MonthNum(string yyyyMM) => int.TryParse(yyyyMM.AsSpan(5, 2), out var m) ? m : 0;
    private static string MonthName(string yyyyMM)
    {
        var m = MonthNum(yyyyMM);
        var year = yyyyMM.Length >= 4 ? yyyyMM[..4] : "";
        return m is >= 1 and <= 12 ? $"{MonthNameNum(m)} de {year}" : yyyyMM;
    }

    private static string OrderStatus(string s) => s switch
    {
        "open" => "Abierto", "partially-shipped" => "Envío parcial", "shipped" => "Enviado",
        "invoiced" => "Facturado", "canceled" => "Cancelado", _ => s
    };
    private static string InvoiceStatus(string s) => s switch
    {
        "overdue" => "Vencida", "paid" => "Cobrada", "partial" => "Parcial",
        "credit" => "A crédito", "pending" => "Pendiente", _ => s
    };

    // Busca en la pregunta el nombre de un artículo del historial del cliente (por
    // coincidencia de una palabra significativa del nombre)
    private static string? MatchProduct(string question, Snapshot s)
    {
        var q = Normalize(question);
        foreach (var prod in s.Purchases.TopProducts)
        {
            foreach (var word in Normalize(prod.Name).Split([' ', '-', '·'], StringSplitOptions.RemoveEmptyEntries))
                if (word.Length >= 4 && q.Contains(word)) return prod.Name;
        }
        return null;
    }

    private static string? ExtractSize(string question)
    {
        var words = question.Split([' ', ',', '.', '?', '¿', ':', ';'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
            if (words[i].Equals("talla", StringComparison.OrdinalIgnoreCase) && i + 1 < words.Length)
                return words[i + 1].ToUpperInvariant();
        foreach (var w in words)
            if (w.Length is >= 1 and <= 3 && w.All(char.IsDigit)) return w;
        return null;
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    public sealed record AssistantTurn(string? Role, string? Content);
    public sealed record AssistantRequest(string? Question, IReadOnlyList<AssistantTurn>? History);
}
