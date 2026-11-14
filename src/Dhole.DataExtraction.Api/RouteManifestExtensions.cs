using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Dhole.RouteDiscovery;

/// <summary>Read-only introspection for Dhole API Manager. It does not publish API routes.</summary>
public static class RouteManifestExtensions
{
    public static void MapDholeRouteManifest(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/dhole/routes", (HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();
            var authorized = context.User.Claims
                .Where(claim => claim.Type is "scope" or "scp" or "scopes")
                .SelectMany(claim => claim.Value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
                .Contains("integrations.thirdparty.manage", StringComparer.OrdinalIgnoreCase);
            if (!authorized) return Results.Forbid();

            var routes = app.DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .SelectMany(route => route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                        ?? Array.Empty<string>(),
                    (route, method) => new
                    {
                        method = method.ToUpperInvariant(),
                        path = route.RoutePattern.RawText ?? string.Empty,
                        description = route.DisplayName ?? string.Empty,
                        parameters = route.RoutePattern.Parameters.Select(p => new
                        {
                            name = p.Name,
                            required = !p.IsOptional && p.Default is null
                        }).ToArray()
                    })
                .Where(item => item.path.StartsWith("/", StringComparison.Ordinal) &&
                    item.path != "/.well-known/dhole/routes" &&
                    (item.method is "GET" or "POST" or "PUT" or "DELETE" or "PATCH"))
                .DistinctBy(item => item.method + " " + item.path)
                .OrderBy(item => item.path, StringComparer.Ordinal)
                .ThenBy(item => item.method, StringComparer.Ordinal)
                .ToArray();
            return Results.Ok(new { schemaVersion = 1, routes });
        }).RequireAuthorization().ExcludeFromDescription();
    }
}
