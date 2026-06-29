namespace Dhole.DataExtraction.Api.Extensions;

public static class HttpContextAuditExtensions
{
    public static Guid? GetCurrentUserId(this HttpContext httpContext)
    {
        var value =
            httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst("user_id")?.Value
            ?? httpContext.User.FindFirst("nameidentifier")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public static string? GetCurrentUserName(this HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirst("name")?.Value
            ?? httpContext.User.FindFirst("preferred_username")?.Value
            ?? httpContext.User.FindFirst("email")?.Value;
    }
}
