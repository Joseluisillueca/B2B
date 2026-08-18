using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace B2B.Api.Auth;

// Autorización del CMS (auditoría B-1). El emisor de tokens es único —el mismo login
// sirve al portal, al conector y al CMS—, así que la separación tiene que estar en el
// rol: solo "admin" entra en /api/admin/*. El sync de BC nunca asigna ese rol (a los
// usuarios de cliente les pone "client-admin"), de modo que ningún token de cliente
// puede llegar al CMS ni a los payloads crudos de otros clientes.
public static class AdminPolicy
{
    public const string Name = "cms-admin";
    public const string Role = "admin";

    public static AuthorizationOptions AddAdminPolicy(this AuthorizationOptions options)
    {
        options.AddPolicy(Name, policy => policy
            .RequireAuthenticatedUser()
            // El claim viaja como "role"; JwtBearer puede además mapearlo a ClaimTypes.Role
            .RequireAssertion(context => context.User.HasClaim(claim =>
                (claim.Type == "role" || claim.Type == ClaimTypes.Role)
                && string.Equals(claim.Value, Role, StringComparison.Ordinal))));
        return options;
    }

    /// Cierra un endpoint del CMS: 401 sin token, 403 con un token que no sea de admin
    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Name);
}
