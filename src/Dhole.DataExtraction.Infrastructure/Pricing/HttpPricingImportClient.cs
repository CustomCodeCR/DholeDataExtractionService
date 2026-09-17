using System.Net.Http.Json;
using System.Text.Json;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.Pricing;

public sealed class HttpPricingImportClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<HttpPricingImportClient> logger
) : IPricingImportClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PricingImportSubmissionResult> SubmitAsync(
        PricingImportSubmissionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var configuredUrl = configuration["Pricing:ImportFromExtractionUrl"]
            ?? configuration["Pricing:RateImportFromExtractionUrl"];

        if (
            string.IsNullOrWhiteSpace(configuredUrl)
            || !Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var endpoint)
        )
        {
            return new PricingImportSubmissionResult(
                false,
                null,
                "Debe configurar Pricing:ImportFromExtractionUrl con una URL absoluta del API de Pricing."
            );
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(request),
        };

        message.Headers.TryAddWithoutValidation(
            "X-Correlation-Id",
            request.Response.CorrelationId
        );

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ReadTimeoutSeconds(configuration)));

        try
        {
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );

            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new PricingImportSubmissionResult(
                    false,
                    null,
                    $"Pricing respondió {(int)response.StatusCode}: {Limit(content)}"
                );
            }

            var pricingImportBatchId = TryReadPricingImportBatchId(content)
                ?? request.PricingImportId;

            logger.LogInformation(
                "La extracción {ExtractionExecutionId} fue enviada a Pricing como lote {PricingImportBatchId}.",
                request.ExtractionExecutionId,
                pricingImportBatchId
            );

            return new PricingImportSubmissionResult(true, pricingImportBatchId, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var messageText = $"Pricing no respondió dentro de {ReadTimeoutSeconds(configuration)} segundos.";
            logger.LogWarning(
                "{Message} Extracción: {ExtractionExecutionId}.",
                messageText,
                request.ExtractionExecutionId
            );
            return new PricingImportSubmissionResult(false, null, messageText);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "No fue posible enviar la extracción {ExtractionExecutionId} a Pricing.",
                request.ExtractionExecutionId
            );
            return new PricingImportSubmissionResult(false, null, exception.Message);
        }
    }

    public async Task<IReadOnlyCollection<PricingLearningExample>> GetLearningContextAsync(
        int limit = 12,
        CancellationToken cancellationToken = default
    )
    {
        var endpoint = ResolveLearningEndpoint(Math.Clamp(limit, 1, 50));
        if (endpoint is null)
        {
            return Array.Empty<PricingLearningExample>();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(ReadTimeoutSeconds(configuration), 30)));

        try
        {
            using var response = await httpClient.GetAsync(endpoint, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Pricing no pudo entregar contexto de aprendizaje. Status {StatusCode}.",
                    (int)response.StatusCode
                );
                return Array.Empty<PricingLearningExample>();
            }

            var envelope = await response.Content.ReadFromJsonAsync<PricingLearningContextEnvelope>(
                JsonOptions,
                timeout.Token
            );

            return envelope?.Examples?
                .Where(example => !string.IsNullOrWhiteSpace(example.Outcome))
                .Take(Math.Clamp(limit, 1, 50))
                .ToArray() ?? Array.Empty<PricingLearningExample>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Expiró la consulta del contexto de aprendizaje de Pricing.");
            return Array.Empty<PricingLearningExample>();
        }
        catch (Exception exception)
        {
            // El aprendizaje es una ayuda de recuperación. Nunca debe impedir una extracción.
            logger.LogWarning(
                exception,
                "No fue posible cargar ejemplos aprobados/rechazados desde Pricing."
            );
            return Array.Empty<PricingLearningExample>();
        }
    }

    private Uri? ResolveLearningEndpoint(int limit)
    {
        var configuredLearningUrl = configuration["Pricing:LearningContextUrl"];
        if (
            !string.IsNullOrWhiteSpace(configuredLearningUrl)
            && Uri.TryCreate(configuredLearningUrl.Trim(), UriKind.Absolute, out var configured)
        )
        {
            return AppendLimit(configured, limit);
        }

        var importUrl = configuration["Pricing:ImportFromExtractionUrl"]
            ?? configuration["Pricing:RateImportFromExtractionUrl"];
        if (
            string.IsNullOrWhiteSpace(importUrl)
            || !Uri.TryCreate(importUrl.Trim(), UriKind.Absolute, out var importEndpoint)
        )
        {
            return null;
        }

        var builder = new UriBuilder(importEndpoint)
        {
            Path = "/api/pricing/rate-import-batches/learning-context",
            Query = $"limit={limit}",
        };
        return builder.Uri;
    }

    private static Uri AppendLimit(Uri endpoint, int limit)
    {
        var builder = new UriBuilder(endpoint);
        var separator = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : "&";
        builder.Query = $"{builder.Query.TrimStart('?')}{separator}limit={limit}";
        return builder.Uri;
    }

    private static Guid? TryReadPricingImportBatchId(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return FindGuid(document.RootElement);
        }
        catch (JsonException)
        {
            return Guid.TryParse(content.Trim().Trim('"'), out var value) ? value : null;
        }
    }

    private static Guid? FindGuid(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Guid.TryParse(element.GetString(), out var stringValue) ? stringValue : null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var candidateName in new[] { "pricingImportBatchId", "importBatchId", "id" })
        {
            var property = element
                .EnumerateObject()
                .FirstOrDefault(x => x.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase));

            if (
                property.Value.ValueKind == JsonValueKind.String
                && Guid.TryParse(property.Value.GetString(), out var candidate)
            )
            {
                return candidate;
            }
        }

        foreach (var wrapperName in new[] { "data", "value", "result" })
        {
            var wrapper = element
                .EnumerateObject()
                .FirstOrDefault(x => x.Name.Equals(wrapperName, StringComparison.OrdinalIgnoreCase));

            if (wrapper.Value.ValueKind != JsonValueKind.Undefined)
            {
                var nested = FindGuid(wrapper.Value);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static int ReadTimeoutSeconds(IConfiguration configuration)
    {
        return int.TryParse(configuration["Pricing:TimeoutSeconds"], out var value) && value > 0
            ? value
            : 60;
    }

    private static string Limit(string content)
    {
        const int maxLength = 4000;
        return content.Length <= maxLength ? content : content[..maxLength];
    }

    private sealed record PricingLearningContextEnvelope(
        IReadOnlyCollection<PricingLearningExample> Examples
    );
}
