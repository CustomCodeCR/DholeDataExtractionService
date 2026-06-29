using Dhole.DataExtraction.Persistence.Auditing;

namespace Dhole.DataExtraction.Api.Middleware;

public sealed class AuditExecutionContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var userId = TryGetUserId(context);
        var userName =
            context.User.Identity?.Name
            ?? context.User.FindFirst("name")?.Value
            ?? context.User.FindFirst("preferred_username")?.Value
            ?? context.User.FindFirst("email")?.Value;

        var ipAddress = context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor)
            ? forwardedFor
                .ToString()
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
            : context.Connection.RemoteIpAddress?.ToString();

        var userAgent = context.Request.Headers.UserAgent.ToString();
        var correlationId = Guid.TryParse(context.TraceIdentifier, out var parsedCorrelationId)
            ? parsedCorrelationId
            : Guid.NewGuid();

        using var _ = AuditExecutionContextAccessor.Begin(
            new AuditExecutionContext(userId, userName, ipAddress, userAgent, correlationId)
        );

        await next(context);
    }

    private static Guid? TryGetUserId(HttpContext context)
    {
        var value =
            context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst("user_id")?.Value
            ?? context.User.FindFirst("nameidentifier")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
