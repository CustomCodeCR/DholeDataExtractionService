using CustomCodeFramework.Auth.DependencyInjection;
using CustomCodeFramework.Mongo.DependencyInjection;
using CustomCodeFramework.Redis.DependencyInjection;
using Dhole.AI.Contracts.Grpc;
using Dhole.Config.Contracts.Grpc;
using Dhole.DataExtraction.Application.Abstractions.Cache;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Files;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Mongo;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Infrastructure.Cache;
using Dhole.DataExtraction.Infrastructure.Email;
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
using Dhole.DataExtraction.Infrastructure.Pricing;
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
        IConfiguration configuration,
        bool includeWebAuthentication = true
    )
    {
        if (includeWebAuthentication)
        {
            services.AddCustomCodeAuth(configuration);

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            });
        }

        services.AddCustomCodeRedis(configuration);
        services.AddCustomCodeMongo(configuration);

        services.AddScoped<IDataExtractionCacheService, DataExtractionCacheService>();
        services.AddScoped<IExtractionFileReader, ExtractionFileReader>();
        services.AddScoped<IExtractionSourceFileStorage, LocalExtractionSourceFileStorage>();

        services.AddScoped<IEmailReader, ImapEmailReader>();
        services.AddScoped<IEmailSecretResolver, EmailSecretResolver>();
        services.AddScoped<IEmailFileStorage, LocalEmailFileStorage>();
        services.AddScoped<IEmailRateClassifier, EmailRateClassifier>();
        services.AddHttpClient<IPricingImportClient, HttpPricingImportClient>();

        services.AddScoped<IDocumentExtractor, ExcelDocumentExtractor>();
        services.AddScoped<IDocumentExtractor, CsvDocumentExtractor>();
        services.AddScoped<IDocumentExtractor, PdfDocumentExtractor>();
        services.AddScoped<IDocumentExtractor, EmailDocumentExtractor>();
        services.AddScoped<IDocumentExtractorFactory, DocumentExtractorFactory>();

        services.AddScoped<IColumnMappingService, ColumnMappingService>();
        services.AddScoped<IPricingRecordNormalizer, PricingRecordNormalizer>();
        services.AddScoped<IPricingCatalogStandardizer, PricingCatalogStandardizer>();
        services.AddScoped<IDataQualityValidator, DataQualityValidator>();
        services.AddScoped<IExtractionPipeline, ExtractionPipeline>();

        services.AddScoped<IExtractionSnapshotWriter, ExtractionSnapshotWriter>();
        services.AddScoped<IAiExtractionClient, AiExtractionGrpcClient>();
        services.AddScoped<IAiEmailContentReader, AiEmailContentReader>();

        var configGrpcAddress = configuration["Grpc:Clients:Config:Address"];
        if (string.IsNullOrWhiteSpace(configGrpcAddress))
        {
            throw new InvalidOperationException(
                "Debe configurar Grpc:Clients:Config:Address para consultar los catálogos."
            );
        }

        services.AddGrpcClient<ConfigCatalogGrpc.ConfigCatalogGrpcClient>(options =>
        {
            options.Address = new Uri(configGrpcAddress);
        });

        services.AddScoped<IConfigCatalogClient, ConfigCatalogGrpcClient>();

        var aiGrpcAddress = configuration["Grpc:Clients:AI:Address"];
        if (string.IsNullOrWhiteSpace(aiGrpcAddress))
        {
            aiGrpcAddress = "http://localhost:5307";
        }

        services.AddGrpcClient<AiExecutionGrpc.AiExecutionGrpcClient>(options =>
        {
            options.Address = new Uri(aiGrpcAddress);
        });

        return services;
    }
}
