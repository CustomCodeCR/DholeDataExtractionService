using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhole.AI.Contracts.Grpc;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Infrastructure.Email;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

/// <summary>
/// Pricing-specific AI client. It keeps the legacy helpers for generic normalization,
/// but owns tariff extraction so every accepted extraction is produced by the required
/// Qwen model and can use approved/rejected Pricing decisions as retrieval feedback.
/// </summary>
public sealed class AdaptivePricingAiExtractionClient(
    AiExtractionGrpcClient legacyClient,
    AiExecutionGrpc.AiExecutionGrpcClient client,
    IPricingImportClient pricingImportClient,
    IConfiguration configuration,
    ILogger<AdaptivePricingAiExtractionClient> logger
) : IAiExtractionClient
{
    private const string DefaultRequiredModel = "qwen3.5:35b-a3b-q4_K_M";
    private const int DefaultTimeoutSeconds = 1_800;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string PricingJsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean" },
            "confidence": { "type": "number", "minimum": 0, "maximum": 100 },
            "rows": {
              "type": "array",
              "maxItems": 250,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "pol": { "type": ["string", "null"] },
                  "poe": { "type": ["string", "null"] },
                  "pod": { "type": ["string", "null"] },
                  "containerType": { "type": ["string", "null"] },
                  "carrier": { "type": ["string", "null"] },
                  "agent": { "type": ["string", "null"] },
                  "commodity": { "type": ["string", "null"] },
                  "currency": { "type": "string", "minLength": 3, "maxLength": 3 },
                  "quantity": { "type": ["integer", "null"], "minimum": 1 },
                  "rateType": { "type": ["string", "null"] },
                  "etd": { "type": ["string", "null"] },
                  "chargeBasis": { "type": ["string", "null"] },
                  "minimum": { "type": ["string", "null"] },
                  "freeDays": { "type": ["integer", "null"], "minimum": 0 },
                  "transitDays": { "type": ["integer", "null"], "minimum": 0 },
                  "validFrom": { "type": ["string", "null"] },
                  "validTo": { "type": ["string", "null"] },
                  "oceanFreight": { "type": ["number", "null"] },
                  "originCharges": { "type": ["number", "null"] },
                  "destinationCharges": { "type": ["number", "null"] },
                  "surcharges": { "type": ["number", "null"] },
                  "totalCost": { "type": ["number", "null"] },
                  "totalSale": { "type": ["number", "null"] },
                  "profit": { "type": ["number", "null"] },
                  "margin": { "type": ["number", "null"] },
                  "spaceComment": { "type": ["string", "null"] },
                  "remarks": { "type": ["string", "null"] }
                },
                "required": [
                  "pol", "poe", "pod", "containerType", "carrier", "agent",
                  "commodity", "currency", "quantity", "rateType", "etd",
                  "chargeBasis", "minimum", "freeDays", "transitDays",
                  "validFrom", "validTo", "oceanFreight", "originCharges",
                  "destinationCharges", "surcharges", "totalCost", "totalSale",
                  "profit", "margin", "spaceComment", "remarks"
                ]
              }
            },
            "warnings": {
              "type": "array",
              "items": { "type": "string" }
            }
          },
          "required": ["success", "confidence", "rows", "warnings"]
        }
        """;

    public Task<AiColumnMappingResult> SuggestColumnMappingsAsync(
        IReadOnlyCollection<string> headers,
        string? rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    ) => legacyClient.SuggestColumnMappingsAsync(headers, rawText, profileCode, cancellationToken);

    public Task<AiTextNormalizationResult> NormalizePricingTextAsync(
        string rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    ) => legacyClient.NormalizePricingTextAsync(rawText, profileCode, cancellationToken);

    public async Task<AiPricingEmailAnalysisResult> AnalyzePricingEmailAsync(
        AiPricingEmailAnalysisRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!ReadBoolean(configuration["AI:EmailFallback:Enabled"], true))
        {
            return Failure(
                "AI.EmailFallbackDisabled",
                "La extracción de IA para Pricing está deshabilitada."
            );
        }

        var requiredModel = FirstText(
            configuration["AI:EmailFallback:RequiredModelExternalId"],
            DefaultRequiredModel
        )!;
        var requireExactModel = ReadBoolean(
            configuration["AI:EmailFallback:RequireExactModel"],
            true
        );
        var profileKey = FirstText(
            configuration["AI:EmailFallback:ProfileKey"],
            "pricing-email-analysis"
        )!;

        var isBodySource = request.SourceType.Contains("Body", StringComparison.OrdinalIgnoreCase);
        var sourceContent = isBodySource
            ? EmailPricingContentSelector.SelectBestPricingSection(request.SourceContent)
            : request.SourceContent;
        sourceContent = LimitPreservingEdges(
            sourceContent ?? string.Empty,
            ReadPositiveInt(
                configuration["AI:EmailFallback:MaximumContentCharacters"],
                12_000
            )
        );

        if (string.IsNullOrWhiteSpace(sourceContent))
        {
            return Failure(
                "AI.EmptyPricingSource",
                "La fuente no contiene texto utilizable para extraer tarifas."
            );
        }

        var learningExamples = await LoadLearningExamplesAsync(cancellationToken);
        var previousRows = request.PreviousRows
            .Where(IsStructurallyUseful)
            .Take(30)
            .ToArray();

        var payload = JsonSerializer.Serialize(
            new
            {
                taskVersion = "pricing-fcl-lcl-template-learning-v1",
                objective =
                    "Extraer, clasificar, normalizar y preparar tarifas para la plantilla oficial de importación de Pricing.",
                requiredModel,
                targetTemplate = new[]
                {
                    "Carrier",
                    "Equipo",
                    "Cantidad",
                    "POL",
                    "POE",
                    "Flete Internacional",
                    "Moneda",
                    "Tipo Tarifa",
                    "ETD",
                    "Válido Desde",
                    "Válido Hasta",
                    "Tiempo Tránsito (días)",
                    "Días Libres en Destino",
                    "Observaciones",
                },
                rules = new[]
                {
                    "Devuelve únicamente JSON que cumpla el esquema. No inventes rutas, navieras, montos, fechas ni equipos.",
                    "sourceContent es la única fuente de hechos para la tarifa actual. Los ejemplos aprendidos nunca autorizan copiar una tarifa que no aparezca en sourceContent.",
                    "Antes de extraer cada bloque o tabla clasifícalo mentalmente como FCL, LCL, INLAND/PRE-CARRIAGE o CHARGES-ONLY.",
                    "FCL: reconoce equipos 20DV/20GP/20STD, 40DV/40GP/40STD, 40HC/40HQ y equivalentes. Normaliza DV/GP/STD a DV y HQ a HC.",
                    "Si la fuente agrupa explícitamente 40DV/40HC o 40'/40HC bajo un único monto, crea una fila 40DV y otra 40HC con ese mismo monto. No dupliques si los valores están separados.",
                    "LCL: reconoce señales W/M, CBM, CFS, RATE PER CBM, OCEAN FREIGHT W/M, MINIMUM o relaciones como 1CBM=1000KG. Usa containerType=LCL.",
                    "En LCL oceanFreight es la tarifa marítima por W/M o CBM cuando esté identificada. Conserva chargeBasis y minimum. No conviertas un total W/M en oceanFreight si existe una columna Ocean Freight W/M separada.",
                    "Las relaciones de cobro LCL son locales a la fila/ruta: conserva exactamente 1CBM=1000KG, 1CBM=500KG u otra relación indicada; nunca impongas una relación global.",
                    "INLAND/PRE-CARRIAGE, TAO, arbitrary y tablas de acarreo no son flete marítimo. Nunca copies esos montos a oceanFreight. Solo vincúlalos como originCharges/remarks cuando existe una tarifa marítima completa y la fuente demuestra que aplican a esa ruta.",
                    "CHARGES-ONLY: DTHC, ISPD, CCL, DOC/BL, VGM, handling, rayos X y similares conservan su base de cobro. No sumes cargos por BL con cargos por contenedor o W/M como si fueran la misma unidad.",
                    "En tablas MSC, OCEAN FREIGHT va a oceanFreight y TOTAL ALL IN va a totalCost. PANAMA LOCAL CHARGES y recargos se separan solo cuando su unidad y aplicación son inequívocas.",
                    "POL es puerto/origen de carga. Destination, Port of Discharge, Arrival, Gateway o un encabezado POD que signifique Port of Discharge se guarda en POE. POD se reserva para Place of Delivery/Final Destination explícito.",
                    "No generes filas a partir de firmas, avisos legales, redes sociales, teléfonos, direcciones, enlaces o texto histórico que no contenga una tarifa vigente.",
                    "Separa filas cuando cambie POL, POE, carrier, equipo, modalidad FCL/LCL, vigencia, oceanFreight o una condición que cambie el precio.",
                    "currency es obligatoria; usa USD únicamente cuando la fuente exprese dólares, símbolo $ en un contexto tarifario en USD o no exista indicio de otra moneda. Si existe otra moneda explícita, consérvala.",
                    "validFrom y validTo deben ser yyyy-MM-dd. Si la fuente da día/mes sin año, usa el año explícito del documento/correo; si no existe, usa processingDateUtc.",
                    "Para la plantilla, mapea Carrier->carrier, Equipo->containerType, Cantidad->quantity, POL->pol, POE->poe, Flete Internacional->oceanFreight, Moneda->currency, Tipo Tarifa->rateType, ETD->etd, vigencias, tránsito, días libres y Observaciones->remarks.",
                    "Cuando Cantidad, Tipo Tarifa, ETD, chargeBasis o minimum existan, extráelos aunque no sean necesarios para validar la tarifa; DataExtraction los conservará en Observaciones para la proyección de plantilla.",
                    "carrier puede ser null para LCL si la fuente solo identifica al consolidador/agente y no una naviera. No conviertas automáticamente Vanguard, Pier17, Globelink u otro co-loader en naviera.",
                    "agent solo si la fuente lo identifica como agente/co-loader/proveedor. No lo deduzcas únicamente del remitente, firma o nombre del archivo.",
                    "Los learningExamples con outcome=approved son referencias positivas de normalización. Los outcome=rejected son ejemplos negativos: evita repetir su patrón cuando el sourceContent actual muestre la misma ambigüedad.",
                    "previousExtraction y previousIssues son retroalimentación del intento actual. Corrige bloqueos contra sourceContent; no repitas valores que ya fueron marcados como incorrectos.",
                    "Si un dato requerido por la plantilla no existe en la fuente, déjalo null y explica la ausencia en warnings; nunca lo fabriques.",
                },
                processingDateUtc = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                request.EmailMessageId,
                request.EmailAttachmentId,
                request.FromAddress,
                request.Subject,
                request.SourceType,
                request.SourceName,
                sourceContentType = EmptyToNull(request.SourceContentType),
                sourceContent,
                emailContext = isBodySource
                    ? null
                    : BuildEmailContext(request.BodyText, request.BodyHtml),
                catalogHints = request.CatalogHints.Select(group => new
                {
                    group = group.GroupSlug,
                    items = group.Items.Select(item => new
                    {
                        item.Name,
                        item.Code,
                        item.Slug,
                    }),
                }),
                learningExamples = learningExamples.Select(example => new
                {
                    outcome = example.Outcome,
                    example.Pol,
                    example.Poe,
                    example.Pod,
                    example.ContainerType,
                    example.Carrier,
                    example.Currency,
                    example.OceanFreight,
                    example.OriginCharges,
                    example.DestinationCharges,
                    example.Surcharges,
                    example.TotalCost,
                    example.FreeDays,
                    example.TransitDays,
                    validFrom = example.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    validTo = example.ValidTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    example.Commodity,
                    example.SpaceComment,
                }),
                previousExtraction = new
                {
                    errorCode = EmptyToNull(request.PreviousErrorCode),
                    errorMessage = EmptyToNull(request.PreviousErrorMessage),
                    confidence = request.PreviousConfidence,
                    rows = previousRows,
                    issues = request.PreviousIssues.Take(40).Select(issue => new
                    {
                        issue.Code,
                        issue.Message,
                        issue.IsBlocking,
                        issue.ColumnName,
                        issue.RawValue,
                    }),
                },
            },
            JsonOptions
        );

        var grpcRequest = new ExecuteAiStructuredGrpcRequest
        {
            ProfileKey = profileKey,
            CorrelationId = request.CorrelationId,
            RequestHash = ComputeSha256(
                string.Concat(
                    payload,
                    "|image:",
                    string.IsNullOrWhiteSpace(request.SourceImageBase64)
                        ? string.Empty
                        : ComputeSha256(request.SourceImageBase64)
                )
            ),
            RequestedByName = "DholeDataExtractionService",
            JsonSchemaOverride = PricingJsonSchema,
        };
        var message = new AiMessageGrpcModel { Role = "user", Content = payload };

        if (
            !string.IsNullOrWhiteSpace(request.SourceImageBase64)
            && !string.IsNullOrWhiteSpace(request.SourceImageMimeType)
        )
        {
            message.Images.Add(new AiImageGrpcModel
            {
                MimeType = request.SourceImageMimeType,
                Base64Data = request.SourceImageBase64,
            });
        }

        grpcRequest.Messages.Add(message);

        try
        {
            var response = await client.ExecuteStructuredAsync(
                grpcRequest,
                deadline: DateTime.UtcNow.AddSeconds(
                    ReadPositiveInt(
                        configuration["AI:EmailFallback:TimeoutSeconds"],
                        DefaultTimeoutSeconds
                    )
                ),
                cancellationToken: cancellationToken
            );

            if (!response.Success)
            {
                return Failure(
                    EmptyToNull(response.ErrorCode) ?? "AI.ExecutionFailed",
                    EmptyToNull(response.ErrorMessage)
                        ?? "AI no pudo analizar la tarifa."
                );
            }

            var actualModel = EmptyToNull(response.SelectedModel?.ExternalModelId);
            if (
                requireExactModel
                && !string.Equals(
                    actualModel,
                    requiredModel,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                logger.LogWarning(
                    "Se rechazó extracción {ExecutionId}: Pricing requiere {RequiredModel} y AI usó {ActualModel}.",
                    response.ExecutionId,
                    requiredModel,
                    actualModel ?? "<sin modelo>"
                );
                return new AiPricingEmailAnalysisResult(
                    false,
                    TryGuid(response.ExecutionId),
                    0m,
                    Array.Empty<AiPricingEmailRow>(),
                    new[]
                    {
                        $"La extracción fue rechazada porque no utilizó {requiredModel}."
                    },
                    "AI.RequiredPricingModelNotUsed",
                    $"Pricing exige {requiredModel} para el 100% de las extracciones; AI respondió con {actualModel ?? "un modelo no identificado"}."
                );
            }

            AdaptivePricingResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<AdaptivePricingResponse>(
                    response.JsonContent,
                    JsonOptions
                );
            }
            catch (JsonException exception)
            {
                logger.LogWarning(
                    exception,
                    "La respuesta estructurada {ExecutionId} no pudo deserializarse.",
                    response.ExecutionId
                );
                return Failure(
                    "AI.InvalidPricingResponse",
                    "AI devolvió JSON válido para el proveedor, pero DataExtraction no pudo convertirlo al contrato de Pricing.",
                    TryGuid(response.ExecutionId)
                );
            }

            var rows = parsed?.Rows?
                .Select(ToApplicationRow)
                .Where(HasPricingData)
                .ToArray() ?? Array.Empty<AiPricingEmailRow>();
            var warnings = parsed?.Warnings?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            var confidence = Math.Clamp(parsed?.Confidence ?? 0m, 0m, 100m);
            var executionId = TryGuid(response.ExecutionId);

            if (parsed is null || !parsed.Success || rows.Length == 0)
            {
                return new AiPricingEmailAnalysisResult(
                    false,
                    executionId,
                    confidence,
                    rows,
                    warnings,
                    "AI.NoPricingRows",
                    warnings.FirstOrDefault()
                        ?? "Qwen no encontró filas de tarifas utilizables en la fuente."
                );
            }

            logger.LogInformation(
                "Extracción adaptativa completada con {Model}. Ejecución {ExecutionId}; filas {Rows}; aprendizaje {LearningExamples} ejemplos.",
                actualModel,
                response.ExecutionId,
                rows.Length,
                learningExamples.Count
            );

            return new AiPricingEmailAnalysisResult(
                true,
                executionId,
                confidence,
                rows,
                warnings
            );
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.DeadlineExceeded)
        {
            return Failure(
                "AI.Timeout",
                "Qwen excedió el tiempo máximo de extracción de Pricing."
            );
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            return Failure(
                "AI.Cancelled",
                "La extracción de Pricing fue cancelada."
            );
        }
    }

    private async Task<IReadOnlyCollection<PricingLearningExample>> LoadLearningExamplesAsync(
        CancellationToken cancellationToken
    )
    {
        var limit = ReadPositiveInt(
            configuration["AI:EmailFallback:LearningExampleLimit"],
            12
        );
        try
        {
            return await pricingImportClient.GetLearningContextAsync(limit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "No fue posible cargar memoria de aprendizaje; la extracción continuará solo con la fuente actual."
            );
            return Array.Empty<PricingLearningExample>();
        }
    }

    private string? BuildEmailContext(string? bodyText, string? bodyHtml)
    {
        var context = EmailPricingContentSelector.SelectPreferredBody(bodyText, bodyHtml);
        if (string.IsNullOrWhiteSpace(context))
        {
            return null;
        }

        return LimitPreservingEdges(
            context,
            ReadPositiveInt(
                configuration["AI:EmailFallback:MaximumEmailContextCharacters"],
                8_000
            )
        );
    }

    private static AiPricingEmailRow ToApplicationRow(AdaptivePricingRow row)
    {
        var remarks = JoinRemarks(
            row.Remarks,
            BuildTemplateMetadata(row)
        );

        return new AiPricingEmailRow(
            EmptyToNull(row.Pol),
            EmptyToNull(row.Poe),
            EmptyToNull(row.Pod),
            EmptyToNull(row.ContainerType),
            EmptyToNull(row.Carrier),
            EmptyToNull(row.Agent),
            EmptyToNull(row.Commodity),
            EmptyToNull(row.Currency)?.ToUpperInvariant() ?? "USD",
            row.FreeDays,
            row.TransitDays,
            ParseDate(row.ValidFrom),
            ParseDate(row.ValidTo),
            row.OceanFreight,
            row.OriginCharges,
            row.DestinationCharges,
            row.Surcharges,
            row.TotalCost,
            row.TotalSale,
            row.Profit,
            row.Margin,
            EmptyToNull(row.SpaceComment),
            remarks
        );
    }

    private static string? BuildTemplateMetadata(AdaptivePricingRow row)
    {
        var values = new List<string>();
        if (row.Quantity.HasValue) values.Add($"TEMPLATE:CANTIDAD={row.Quantity.Value}");
        if (!string.IsNullOrWhiteSpace(row.RateType)) values.Add($"TEMPLATE:TIPO_TARIFA={row.RateType.Trim()}");
        if (!string.IsNullOrWhiteSpace(row.Etd)) values.Add($"TEMPLATE:ETD={row.Etd.Trim()}");
        if (!string.IsNullOrWhiteSpace(row.ChargeBasis)) values.Add($"LCL:BASE_COBRO={row.ChargeBasis.Trim()}");
        if (!string.IsNullOrWhiteSpace(row.Minimum)) values.Add($"LCL:MINIMO={row.Minimum.Trim()}");
        return values.Count == 0 ? null : string.Join("; ", values);
    }

    private static bool HasPricingData(AiPricingEmailRow row) =>
        !string.IsNullOrWhiteSpace(row.OriginPort)
        || !string.IsNullOrWhiteSpace(row.PortOfExit)
        || !string.IsNullOrWhiteSpace(row.ContainerType)
        || row.OceanFreight.HasValue
        || row.TotalCost.HasValue
        || row.TotalSale.HasValue;

    private static bool IsStructurallyUseful(AiPricingEmailRow row) =>
        (!string.IsNullOrWhiteSpace(row.OriginPort)
            || !string.IsNullOrWhiteSpace(row.PortOfExit))
        && (row.OceanFreight.HasValue || row.TotalCost.HasValue || row.TotalSale.HasValue);

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (
            DateTime.TryParseExact(
                value.Trim(),
                new[] { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var exact
            )
        )
        {
            return exact.Date;
        }

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed
        )
            ? parsed.Date
            : null;
    }

    private static string? JoinRemarks(params string?[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().TrimEnd(';'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? null : string.Join("; ", parts);
    }

    private static string LimitPreservingEdges(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        const string marker = "\n[CONTENIDO INTERMEDIO OMITIDO]\n";
        if (maxLength <= marker.Length + 2) return value[..maxLength];
        var available = maxLength - marker.Length;
        var head = available * 3 / 4;
        return value[..head] + marker + value[^(available - head)..];
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static Guid? TryGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static int ReadPositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static bool ReadBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static AiPricingEmailAnalysisResult Failure(
        string errorCode,
        string errorMessage,
        Guid? executionId = null
    ) => new(
        false,
        executionId,
        0m,
        Array.Empty<AiPricingEmailRow>(),
        new[] { errorMessage },
        errorCode,
        errorMessage
    );

    private sealed record AdaptivePricingResponse(
        bool Success,
        decimal Confidence,
        IReadOnlyCollection<AdaptivePricingRow> Rows,
        IReadOnlyCollection<string> Warnings
    );

    private sealed record AdaptivePricingRow(
        string? Pol,
        string? Poe,
        string? Pod,
        string? ContainerType,
        string? Carrier,
        string? Agent,
        string? Commodity,
        string? Currency,
        int? Quantity,
        string? RateType,
        string? Etd,
        string? ChargeBasis,
        string? Minimum,
        int? FreeDays,
        int? TransitDays,
        string? ValidFrom,
        string? ValidTo,
        decimal? OceanFreight,
        decimal? OriginCharges,
        decimal? DestinationCharges,
        decimal? Surcharges,
        decimal? TotalCost,
        decimal? TotalSale,
        decimal? Profit,
        decimal? Margin,
        string? SpaceComment,
        string? Remarks
    );
}
