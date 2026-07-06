using CustomCodeFramework.Auth.DependencyInjection;
using CustomCodeFramework.Mongo.DependencyInjection;
using CustomCodeFramework.Redis.DependencyInjection;
using Dhole.DataExtraction.Application.Abstractions.Cache;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Files;
using Dhole.DataExtraction.Application.Abstractions.Mongo;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Infrastructure.Cache;
using Dhole.DataExtraction.Infrastructure.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Csv;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Extraction.Excel;
using Dhole.DataExtraction.Infrastructure.Extraction.Pdf;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Infrastructure.GrpcClients;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Dhole.DataExtraction.Infrastructure.Mongo;
using Dhole.DataExtraction.Infrastructure.Normalization;
using Dhole.DataExtraction.Infrastructure.Pipeline;
using Dhole.DataExtraction.Infrastructure.Quality;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.DataExtraction.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeAuth(configuration);

        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        services.AddCustomCodeRedis(configuration);
        services.AddCustomCodeMongo(configuration);

        services.AddScoped<IDataExtractionCacheService, DataExtractionCacheService>();
        services.AddScoped<IExtractionFileReader, ExtractionFileReader>();

        services.AddScoped<IDocumentExtractor, ExcelDocumentExtractor>();
        services.AddScoped<IDocumentExtractor, CsvDocumentExtractor>();
        services.AddScoped<IDocumentExtractor, PdfDocumentExtractor>();
        services.AddScoped<IDocumentExtractor, EmailDocumentExtractor>();
        services.AddScoped<IDocumentExtractorFactory, DocumentExtractorFactory>();

        services.AddScoped<IColumnMappingService, ColumnMappingService>();
        services.AddScoped<IPricingRecordNormalizer, PricingRecordNormalizer>();
        services.AddScoped<IDataQualityValidator, DataQualityValidator>();
        services.AddScoped<IExtractionPipeline, ExtractionPipeline>();

        services.AddScoped<IExtractionSnapshotWriter, ExtractionSnapshotWriter>();
        services.AddScoped<IAiExtractionClient, AiExtractionGrpcClient>();
        services.AddScoped<IConfigCatalogClient, ConfigCatalogGrpcClient>();

        return services;
    }
}
