using CustomCodeFramework.Core.Abstractions;
using Dhole.DataExtraction.Infrastructure.Time;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Persistence.DependencyInjection;
using Dhole.DataExtraction.Persistence.Seeding;
using Dhole.DataExtraction.Workers.DependencyInjection;
using Dhole.DataExtraction.Workers.Security;
using Microsoft.EntityFrameworkCore;

var contentRoot = Path.Combine(
    Directory.GetCurrentDirectory(),
    "src",
    "Dhole.DataExtraction.Workers"
);

if (!Directory.Exists(contentRoot))
{
    contentRoot = Directory.GetCurrentDirectory();
}

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot }
);

builder.Configuration.Sources.Clear();

builder
    .Configuration.SetBasePath(contentRoot)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        optional: true,
        reloadOnChange: true
    );

if (builder.Environment.IsDevelopment())
{
    // Host.CreateApplicationBuilder carga User Secrets automáticamente, pero este
    // worker limpia las fuentes para usar su propio content root. Hay que volver
    // a agregarlos explícitamente antes de las variables de entorno.
    builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
}

builder.Configuration.AddEnvironmentVariables();

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();

builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddDataExtractionWorker(builder.Configuration);

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
    await dbContext.Database.MigrateAsync();
    await EmailIngestionAccountSeeder.SynchronizeAsync(dbContext, builder.Configuration);
}

await host.RunAsync();
