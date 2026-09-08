using CustomCodeFramework.Core.Abstractions;
using Dhole.DataExtraction.Api.Endpoints;
using Dhole.DataExtraction.Api.Endpoints.Emails;
using Dhole.DataExtraction.Api.Endpoints.Internal;
using Dhole.DataExtraction.Api.Grpc;
using Dhole.DataExtraction.Application.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.Time;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Persistence.DependencyInjection;
using Dhole.DataExtraction.Persistence.Seeding;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Docker loads flat variables from /opt/dhole/.env or /opt/dhole/.env.staging.
// Map the DataExtraction aliases before any service reads the nested configuration.
builder.Configuration.AddInMemoryCollection(
    DataExtractionEnvironmentConfiguration.BuildOverrides(builder.Configuration)
);

const string CorsPolicyName = "data-extraction-cors";

var maxMessageSizeBytes = ReadPositiveInt(
    builder.Configuration["Grpc:Server:MaxMessageSizeBytes"],
    64 * 1024 * 1024
);

var httpPort = ReadPositiveInt(
    builder.Configuration["Http:Port"]
        ?? builder.Configuration["DataExtraction:HttpPort"]
        ?? builder.Configuration["DataExtraction:Port"],
    5205
);

var grpcPort = ReadPositiveInt(
    builder.Configuration["Grpc:Server:Port"]
        ?? builder.Configuration["DataExtraction:GrpcPort"],
    5306
);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxMessageSizeBytes;

    // Browser/REST endpoints such as /health stay on HTTP/1.1.
    options.ListenAnyIP(httpPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });

    // gRPC over local cleartext needs a dedicated HTTP/2-only endpoint.
    // Do not use Http1AndHttp2 without TLS/ALPN, otherwise Kestrel answers
    // HTTP_1_1_REQUIRED and Pricing fails the gRPC call.
    options.ListenAnyIP(grpcPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        CorsPolicyName,
        policy =>
        {
            var allowedOrigins = builder
                .Configuration.GetSection("Cors:AllowedOrigins")
                .Get<string[]>();

            if (allowedOrigins is { Length: > 0 })
            {
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
            }
            else
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            }
        }
    );
});

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = maxMessageSizeBytes;
    options.MaxSendMessageSize = maxMessageSizeBytes;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

var emailIngestionEnabled = DataExtractionEnvironmentConfiguration.IsEmailIngestionEnabled(
    builder.Configuration
);

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet(
    "/health",
    () =>
        Results.Ok(
            new
            {
                status = "Healthy",
                service = "DholeDataExtractionService",
                httpPort,
                grpcPort,
                emailIngestionEnabled,
            }
        )
);

// Keep the management/history API available even when automatic polling is disabled.
// EmailIngestion:Enabled controls ingestion processing, not whether the Web UI can read
// accounts, messages and extraction jobs. Hiding the routes produced misleading 404s.
app.MapEmailIngestionEndpoints();

app.MapInternalAiEmailRequestEndpoints();
app.MapInternalEnvironmentRecoveryEndpoints();
app.MapTabularExtractionEndpoints();

app.MapGrpcService<DataExtractionGrpcService>();

// The email tables and the env-backed account must exist independently of the polling
// toggle. Staging can keep polling disabled while still preserving the configured account.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
    await dbContext.Database.MigrateAsync();
    await EnvironmentDataSeeder.SynchronizeAsync(dbContext, builder.Configuration);

    if (emailIngestionEnabled)
    {
        await EmailIngestionAccountSeeder.SynchronizeAsync(dbContext, builder.Configuration);
    }
}

app.Run();

static int ReadPositiveInt(string? value, int fallback)
{
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
