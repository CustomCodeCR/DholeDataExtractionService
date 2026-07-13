using CustomCodeFramework.Core.Abstractions;
using Dhole.DataExtraction.Api.Endpoints.Emails;
using Dhole.DataExtraction.Api.Grpc;
using Dhole.DataExtraction.Application.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.Time;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Persistence.DependencyInjection;
using Dhole.DataExtraction.Persistence.Seeding;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
            }
        )
);

app.MapEmailIngestionEndpoints();

app.MapGrpcService<DataExtractionGrpcService>();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
    await dbContext.Database.MigrateAsync();
    await EmailIngestionAccountSeeder.SynchronizeAsync(dbContext, builder.Configuration);
}

app.Run();

static int ReadPositiveInt(string? value, int fallback)
{
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
