using System.Security.Claims;
using B2B.Api.Portal;
using Microsoft.AspNetCore.Authorization;

namespace B2B.Api.Auth;

// Autorización del modelo de agente (Fase 1). Las rutas /api/agent/* —cartera de
// clientes del comercial y suplantación— solo las abren el rol "agent" (el que el
// sync `agent` asigna) y "admin" (superusuario de la plataforma). Un token de
// cliente ("client-admin") o de integración nunca entra: 403 con token, 401 sin él.
public static class AgentPolicy
{
    public const string Name = "portal-agent";

    public static readonly string[] Roles = [ClientIdentity.AgentRole, AdminPolicy.Role];

    public static AuthorizationOptions AddAgentPolicy(this AuthorizationOptions options)
    {
        options.AddPolicy(Name, policy => policy
            .RequireAuthenticatedUser()
            // El claim viaja como "role"; JwtBearer puede además mapearlo a ClaimTypes.Role
            .RequireAssertion(context => context.User.Claims.Any(claim =>
                (claim.Type == "role" || claim.Type == ClaimTypes.Role)
                && Roles.Contains(claim.Value))));
        return options;
    }

    /// Cierra una ruta del agente: 401 sin token, 403 con un token que no sea de comercial
    public static TBuilder RequireAgent<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Name);
}
