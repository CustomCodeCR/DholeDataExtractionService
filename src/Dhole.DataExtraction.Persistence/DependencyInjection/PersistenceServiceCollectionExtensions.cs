using CustomCodeFramework.Postgres.DependencyInjection;
using CustomCodeFramework.Postgres.EntityFramework.DependencyInjection;
using Dhole.DataExtraction.Application.Abstractions.Auditing;
using Dhole.DataExtraction.Application.Abstractions.Messaging;
using Dhole.DataExtraction.Application.Abstractions.Repositories;
using Dhole.DataExtraction.Persistence.Auditing;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Persistence.Messaging;
using Dhole.DataExtraction.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.DataExtraction.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodePostgres(configuration);
        services.AddCustomCodePostgresEntityFramework<ServiceDbContext>();

        services.AddScoped<IExtractionExecutionRepository, ExtractionExecutionRepository>();
        services.AddScoped<ISourceDocumentRepository, SourceDocumentRepository>();
        services.AddScoped<IPricingExtractionRecordRepository, PricingExtractionRecordRepository>();
        services.AddScoped<IExtractionIssueRepository, ExtractionIssueRepository>();
        services.AddScoped<IColumnMappingProfileRepository, ColumnMappingProfileRepository>();

        services.AddScoped<IIntegrationEventOutboxWriter, IntegrationEventOutboxWriter>();
        services.AddScoped<IDataExtractionAuditService, DataExtractionAuditService>();

        return services;
    }
}
