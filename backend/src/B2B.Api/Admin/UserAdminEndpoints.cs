using B2B.Api.Auth;
using B2B.Api.Data;
using B2B.Api.Notifications;
using B2B.Api.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Admin;

// Alta y gestión de ACCESOS del portal desde el CMS (portal autónomo). Sin ERP, es el
// admin quien crea los usuarios que entran al portal: administradores del CMS, usuarios
// de cada cliente (con su contraseña o por email de activación) y comerciales. El
// conector, cuando existe, sigue provisionando los suyos por su cuenta (ClientIdentity).
public static class UserAdminEndpoints
{
    // Roles que el CMS puede asignar (mismos que usa el sistema de políticas)
    private static readonly HashSet<string> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        AdminPolicy.Role, ClientIdentity.ClientAdminRole, ClientIdentity.AgentRole, ClientIdentity.IntegrationRole,
    };

    public static void MapUserAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/users", async (AppDbContext db) =>
        {
            var items = await db.Users
                .OrderBy(u => u.Email)
                .Select(u => new
                {
                    id = u.Id,
                    email = u.Email,
                    role = u.Role,
                    name = u.Name,
                    culture = u.Culture,
                    clientExternalId = u.ClientExternalId,
                    clientNumber = u.ClientNumber,
                    agentExternalId = u.AgentExternalId,
                    isActive = u.IsActive,
                    hasPassword = u.PasswordHash != "",
                    activationEmailSentAt = u.ActivationEmailSentAt,
                })
                .ToListAsync();
            return Results.Ok(new { items });
        }).RequireAdmin();

        // Alta de acceso. Si se manda contraseña, el usuario entra ya; si no, nace sin
        // ella y se le puede enviar el email de activación (sendActivation:true).
        app.MapPost("/api/admin/users", async (
            UserUpsertRequest body, AppDbContext db, ActivationService activation) =>
        {
            var email = (body.Email ?? "").Trim().ToLowerInvariant();
            if (email.Length == 0 || !email.Contains('@'))
                return Results.BadRequest(new { error = "Email no válido." });
            var role = (body.Role ?? "").Trim().ToLowerInvariant();
            if (!Roles.Contains(role))
                return Results.BadRequest(new { error = "Rol no válido.", allowed = Roles });
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { error = "Ya existe un usuario con ese email." });

            var user = new AppUser { Id = Guid.NewGuid(), Email = email, PasswordHash = "", Role = role };
            Apply(user, body);
            if (!string.IsNullOrWhiteSpace(body.Password))
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, body.Password!);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            if (body.SendActivation == true && user.PasswordHash == "")
            {
                await activation.SendAsync(user, ActivationPurpose.Activation);
                user.ActivationEmailSentAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.Ok(Projection(user));
        }).RequireAdmin();

        app.MapPut("/api/admin/users/{id:guid}", async (
            Guid id, UserUpsertRequest body, AppDbContext db, ActivationService activation) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound(new { error = "El usuario no existe." });

            if (!string.IsNullOrWhiteSpace(body.Role))
            {
                var role = body.Role!.Trim().ToLowerInvariant();
                if (!Roles.Contains(role)) return Results.BadRequest(new { error = "Rol no válido." });
                user.Role = role;
            }
            Apply(user, body);
            if (body.IsActive is { } active) user.IsActive = active;
            if (!string.IsNullOrWhiteSpace(body.Password))
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, body.Password!);
            await db.SaveChangesAsync();

            if (body.SendActivation == true && user.PasswordHash == "")
            {
                await activation.SendAsync(user, ActivationPurpose.Activation);
                user.ActivationEmailSentAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.Ok(Projection(user));
        }).RequireAdmin();

        app.MapDelete("/api/admin/users/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound(new { error = "El usuario no existe." });
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();
    }

    private static void Apply(AppUser user, UserUpsertRequest body)
    {
        if (body.Name is not null) user.Name = body.Name.Trim();
        if (!string.IsNullOrWhiteSpace(body.Culture)) user.Culture = body.Culture!.Trim();
        // Vínculo con cliente (usuario de cliente) o comercial (agente), según el rol
        if (body.ClientExternalId is not null)
            user.ClientExternalId = body.ClientExternalId.Trim() is { Length: > 0 } c ? c : null;
        if (body.AgentExternalId is not null)
            user.AgentExternalId = body.AgentExternalId.Trim() is { Length: > 0 } a ? a : null;
    }

    private static object Projection(AppUser u) => new
    {
        id = u.Id, email = u.Email, role = u.Role, name = u.Name, culture = u.Culture,
        clientExternalId = u.ClientExternalId, agentExternalId = u.AgentExternalId,
        isActive = u.IsActive, hasPassword = u.PasswordHash != "",
    };
}

public sealed record UserUpsertRequest(
    string? Email, string? Role, string? Name, string? Culture,
    string? ClientExternalId, string? AgentExternalId,
    string? Password, bool? SendActivation, bool? IsActive);
