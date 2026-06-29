using System.Text.Json;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.DataExtraction.Persistence.Auditing;
using Dhole.DataExtraction.Persistence.DbContexts;

namespace Dhole.DataExtraction.Api.Middleware;

public sealed class AuditEndpointMiddleware(
    RequestDelegate next,
    ILogger<AuditEndpointMiddleware> logger
)
{
    private const string SourceService = "DholeDataExtractionService";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] IgnoredPathPrefixes = ["/swagger", "/health", "/metrics", "/favicon.ico"];
    private static readonly string[] EntityIdKeys = ["id", "extractionExecutionId", "pricingImportId"];

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (!ShouldAudit(context))
        {
            return;
        }

        try
        {
            var dbContext = context.RequestServices.GetService<ServiceDbContext>();
            if (dbContext is null)
            {
                return;
            }

            var auditContext = AuditExecutionContextAccessor.Current;
            var correlationId = auditContext?.CorrelationId ?? Guid.NewGuid();
            var eventId = Guid.NewGuid();

            var payload = new
            {
                EventId = eventId,
                CorrelationId = correlationId,
                SourceService,
                EntityType = ResolveEntityType(context),
                EntityId = ResolveEntityId(context),
                Action = ResolveAction(context),
                EventType = ResolveEventType(context),
                UserId = auditContext?.UserId,
                UserName = auditContext?.UserName,
                IpAddress = auditContext?.IpAddress,
                UserAgent = auditContext?.UserAgent,
                OccurredAt = DateTime.UtcNow,
                BeforeJson = (string?)null,
                AfterJson = (string?)null,
                PayloadJson = JsonSerializer.Serialize(
                    new
                    {
                        Method = context.Request.Method,
                        Path = context.Request.Path.Value,
                        QueryString = context.Request.QueryString.Value,
                        StatusCode = context.Response.StatusCode,
                        Endpoint = context.GetEndpoint()?.DisplayName,
                    },
                    JsonOptions
                ),
                Metadata = JsonSerializer.Serialize(
                    new
                    {
                        RouteValues = context.Request.RouteValues.ToDictionary(x => x.Key, x => x.Value?.ToString()),
                        Query = context.Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString()),
                        TraceIdentifier = context.TraceIdentifier,
                    },
                    JsonOptions
                ),
                ErrorMessage = context.Response.StatusCode >= 400
                    ? $"HTTP {context.Response.StatusCode}"
                    : null,
                StackTrace = (string?)null,
                Details = Array.Empty<object>(),
            };

            dbContext.OutboxMessages.Add(
                new OutboxMessage
                {
                    EventId = Guid.NewGuid(),
                    EventType = "Dhole.AuditLogs.Contracts.AuditEvents.RegisterAuditEventRequest",
                    EventName = "audit.event.registered",
                    SourceService = SourceService,
                    PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
                    HeadersJson = null,
                    CorrelationId = correlationId.ToString(),
                    Status = OutboxMessageStatus.Pending,
                    RetryCount = 0,
                    ErrorMessage = null,
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );

            await dbContext.SaveChangesAsync(context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to create technical audit event for {Method} {Path}.",
                context.Request.Method,
                context.Request.Path.Value
            );
        }
    }

    private static bool ShouldAudit(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            return false;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (IgnoredPathPrefixes.Any(x => path.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return context.Response.StatusCode == StatusCodes.Status401Unauthorized
            || context.Response.StatusCode == StatusCodes.Status403Forbidden
            || context.Response.StatusCode >= StatusCodes.Status500InternalServerError;
    }

    private static string ResolveAction(HttpContext context)
    {
        return context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status403Forbidden => "forbidden",
            >= StatusCodes.Status500InternalServerError => "http_error",
            _ => "http_event",
        };
    }

    private static string ResolveEventType(HttpContext context)
    {
        return context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => "auth.access.unauthorized",
            StatusCodes.Status403Forbidden => "auth.access.forbidden",
            >= StatusCodes.Status500InternalServerError => "data-extraction.http.error",
            _ => "data-extraction.http.event",
        };
    }

    private static string? ResolveEntityType(HttpContext context)
    {
        var segments = context.Request.Path.Value?.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is null || segments.Length == 0)
        {
            return null;
        }

        return segments.LastOrDefault();
    }

    private static Guid? ResolveEntityId(HttpContext context)
    {
        foreach (var key in EntityIdKeys)
        {
            if (context.Request.RouteValues.TryGetValue(key, out var routeValue)
                && Guid.TryParse(routeValue?.ToString(), out var routeGuid))
            {
                return routeGuid;
            }

            if (context.Request.Query.TryGetValue(key, out var queryValue)
                && Guid.TryParse(queryValue.ToString(), out var queryGuid))
            {
                return queryGuid;
            }
        }

        foreach (var segment in context.Request.Path.Value?.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            if (Guid.TryParse(segment, out var guid))
            {
                return guid;
            }
        }

        return null;
    }
}
