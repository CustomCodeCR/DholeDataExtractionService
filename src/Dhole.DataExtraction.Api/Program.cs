using CustomCodeFramework.Api.DependencyInjection;
using CustomCodeFramework.Api.Swagger;
using CustomCodeFramework.Core.Abstractions;
using Dhole.DataExtraction.Api.Endpoints;
using Dhole.DataExtraction.Api.Grpc;
using Dhole.DataExtraction.Api.Middleware;
using Dhole.DataExtraction.Application.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.Time;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Persistence.DependencyInjection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicyName = "DholeWebCors";

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddCustomCodeApiWithSwagger(title: "Dhole Data Extraction Service", version: "v1");
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        CorsPolicyName,
        policy =>
        {
            policy
                .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    );
});

builder.Services.AddGrpc();
builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseCustomCodeApi();
app.UseCors(CorsPolicyName);

if (app.Environment.IsDevelopment())
{
    app.UseCustomCodeSwagger();
    app.MapDevExtractionTestEndpoints();
}

app.MapGet(
        "/health",
        () =>
            Results.Ok(
                new
                {
                    service = "DholeDataExtractionService",
                    status = "Healthy",
                    timestamp = DateTimeOffset.UtcNow,
                }
            )
    )
    .AllowAnonymous();

app.UseAuthentication();
app.UseMiddleware<AuditExecutionContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<AuditEndpointMiddleware>();

app.MapGrpcService<DataExtractionGrpcService>();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();
