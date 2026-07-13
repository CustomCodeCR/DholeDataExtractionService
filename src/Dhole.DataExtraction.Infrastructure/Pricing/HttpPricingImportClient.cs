using System.Net.Http.Headers;
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

        message.Headers.TryAddWithoutValidation("X-Correlation-Id", request.Response.CorrelationId);
        message.Headers.TryAddWithoutValidation("X-Source-Service", "DholeDataExtractionService");
        ApplyAuthentication(message);

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

    private void ApplyAuthentication(HttpRequestMessage message)
    {
        var bearerToken = configuration["Pricing:BearerToken"]
            ?? configuration["Pricing:ServiceToken"];

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                bearerToken.Trim()
            );
        }

        var apiKey = configuration["Pricing:ApiKey"]
            ?? configuration["Auth:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var headerName = configuration["Pricing:ApiKeyHeader"];
            if (string.IsNullOrWhiteSpace(headerName))
            {
                headerName = "X-Api-Key";
            }

            message.Headers.TryAddWithoutValidation(headerName.Trim(), apiKey.Trim());
        }
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
}
