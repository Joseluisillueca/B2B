using System.Security.Claims;
using B2B.Api.Portal;
using Microsoft.AspNetCore.Authorization;

namespace B2B.Api.Auth;

// Autorización del conector de BC (auditoría B-2). /api/sync/* y /api/query/* llevaban
// RequireAuthorization() pelado, así que el token "client-admin" de cualquier cliente del
// portal —el rol que el sync asigna a todos— abría la ingesta y las lecturas del conector:
// pedidos crudos sin filtrar, escritura de datos maestros y toma de control de la cuenta de
// otro cliente. Estas rutas son del conector: solo "integration" (la cuenta de servicio del
// Setup) y "admin" (superusuario de la plataforma) entran. El sync nunca asigna esos roles.
public static class ConnectorPolicy
{
    public const string Name = "bc-connector";

    public static readonly string[] Roles = [ClientIdentity.IntegrationRole, AdminPolicy.Role];

    public static AuthorizationOptions AddConnectorPolicy(this AuthorizationOptions options)
    {
        options.AddPolicy(Name, policy => policy
            .RequireAuthenticatedUser()
            // El claim viaja como "role"; JwtBearer puede además mapearlo a ClaimTypes.Role
            .RequireAssertion(context => context.User.Claims.Any(claim =>
                (claim.Type == "role" || claim.Type == ClaimTypes.Role)
                && Roles.Contains(claim.Value))));
        return options;
    }

    /// Cierra una ruta del conector: 401 sin token, 403 con un token que no sea de servicio
    public static TBuilder RequireConnector<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Name);
}
