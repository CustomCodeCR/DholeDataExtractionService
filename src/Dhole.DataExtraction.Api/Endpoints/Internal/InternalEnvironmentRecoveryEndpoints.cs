using System.Security.Cryptography;
using System.Text;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Api.Endpoints.Internal;

public static class InternalEnvironmentRecoveryEndpoints
{
    private const string ServiceKeyHeader = "X-Internal-Service-Key";

    public static IEndpointRouteBuilder MapInternalEnvironmentRecoveryEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        app.MapPost(
                "/api/internal/data-extraction/environment-reseed",
                ReseedEnvironmentAsync
            )
            .WithTags("Internal environment recovery")
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> ReseedEnvironmentAsync(
        HttpContext httpContext,
        IConfiguration configuration,
        ServiceDbContext dbContext,
        IHostEnvironment hostEnvironment,
        CancellationToken cancellationToken
    )
    {
        if (!HasValidServiceKey(httpContext, configuration))
        {
            return Results.Unauthorized();
        }

        await dbContext.Database.MigrateAsync(cancellationToken);

        var seed = await EnvironmentDataSeeder.SynchronizeAsync(
            dbContext,
            configuration,
            cancellationToken
        );

        if (DataExtractionEnvironmentConfiguration.IsEmailIngestionEnabled(configuration))
        {
            await EmailIngestionAccountSeeder.SynchronizeAsync(
                dbContext,
                configuration,
                cancellationToken
            );
        }

        return Results.Ok(
            new
            {
                environment = hostEnvironment.EnvironmentName,
                emailAccountRestored = seed.EmailAccountRestored,
                emailAddress = seed.EmailAddress,
                emailEnabled = seed.EmailEnabled,
                secretValuesReturned = false,
                completedAtUtc = DateTimeOffset.UtcNow,
            }
        );
    }

    private static bool HasValidServiceKey(HttpContext httpContext, IConfiguration configuration)
    {
        var expected = configuration["INTERNAL_SERVICE_KEY"]
            ?? configuration["InternalServices:ServiceKey"];
        var provided = httpContext.Request.Headers[ServiceKeyHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
