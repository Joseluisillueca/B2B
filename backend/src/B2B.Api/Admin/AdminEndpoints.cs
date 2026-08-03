using B2B.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// Endpoints del CMS de administración. Por ahora, la vista de comunicación:
// qué ha sincronizado BC, de qué tipo y cuándo (equivalente al CMS actual).
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/sync-documents",
            async (AppDbContext db, string? entityType, int skip = 0, int take = 50) =>
        {
            var query = db.SyncDocuments.AsQueryable();
            if (!string.IsNullOrEmpty(entityType))
                query = query.Where(d => d.EntityType == entityType);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(d => d.LastReceivedAt)
                .Skip(Math.Max(skip, 0))
                .Take(Math.Clamp(take, 1, 200))
                .Select(d => new
                {
                    d.EntityType,
                    d.ExternalId,
                    d.ParentId,
                    d.FirstReceivedAt,
                    d.LastReceivedAt
                })
                .ToListAsync();

            return Results.Ok(new { total, items });
        }).RequireAuthorization();

        app.MapGet("/api/admin/summary", async (AppDbContext db) =>
        {
            var items = await db.SyncDocuments
                .GroupBy(d => d.EntityType)
                .Select(g => new
                {
                    EntityType = g.Key,
                    Count = g.Count(),
                    LastReceivedAt = g.Max(d => d.LastReceivedAt)
                })
                .OrderBy(g => g.EntityType)
                .ToListAsync();

            return Results.Ok(new { items });
        }).RequireAuthorization();

        app.MapGet("/api/admin/sync-documents/{entityType}/{externalId}",
            async (string entityType, string externalId, AppDbContext db) =>
        {
            var doc = await db.SyncDocuments
                .SingleOrDefaultAsync(d => d.EntityType == entityType && d.ExternalId == externalId);
            if (doc is null)
                return Results.NotFound(new { error = "Document not found" });

            return Results.Ok(new
            {
                entityType = doc.EntityType,
                externalId = doc.ExternalId,
                parentId = doc.ParentId,
                firstReceivedAt = doc.FirstReceivedAt,
                lastReceivedAt = doc.LastReceivedAt,
                payload = doc.Payload
            });
        }).RequireAuthorization();
    }
}
