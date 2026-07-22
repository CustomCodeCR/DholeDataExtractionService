using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.AI.Contracts.Grpc;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

public sealed class AiExtractionGrpcClient(
    AiExecutionGrpc.AiExecutionGrpcClient client,
    IConfiguration configuration,
    ILogger<AiExtractionGrpcClient> logger
) : IAiExtractionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string PricingEmailJsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean" },
            "confidence": { "type": "number", "minimum": 0, "maximum": 100 },
            "rows": {
              "type": "array",
              "maxItems": 200,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "originPort": { "type": ["string", "null"] },
                  "portOfExit": { "type": ["string", "null"] },
                  "destinationPort": { "type": ["string", "null"] },
                  "containerType": { "type": ["string", "null"] },
                  "carrier": { "type": ["string", "null"] },
                  "agent": { "type": ["string", "null"] },
                  "commodity": { "type": ["string", "null"] },
                  "currency": { "type": ["string", "null"] },
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
                  "originPort", "destinationPort", "containerType",
                  "carrier", "currency", "oceanFreight"
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
    )
    {
        return Task.FromResult(
            new AiColumnMappingResult(true, Array.Empty<AiColumnMappingItem>())
        );
    }

    public Task<AiTextNormalizationResult> NormalizePricingTextAsync(
        string rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(new AiTextNormalizationResult(true, rawText));
    }

    public async Task<AiPricingEmailAnalysisResult> AnalyzePricingEmailAsync(
        AiPricingEmailAnalysisRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!ReadBoolean(configuration["AI:EmailFallback:Enabled"], true))
        {
            return Failure("AI.EmailFallbackDisabled", "El fallback de IA para correos está deshabilitado.");
        }

        var profileKey = configuration["AI:EmailFallback:ProfileKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            profileKey = "pricing-email-analysis";
        }

        var isBodySource = request.SourceType.Contains(
            "Body",
            StringComparison.OrdinalIgnoreCase
        );
        var sourceContent = LimitText(EmptyToNull(request.SourceContent));
        var emailContext = isBodySource
            ? null
            : BuildEmailContext(request.BodyText, request.BodyHtml);

        var payload = JsonSerializer.Serialize(
            new
            {
                task = "Extrae filas reales de tarifas FCL y copia los valores de catálogo tal como aparecen en la fuente. No inventes ni sustituyas POL, POE, POD, naviera, agente, contenedor o moneda; DataExtraction los comparará contra Config. Responde solo con el JSON del esquema.",
                emailMessageId = request.EmailMessageId,
                emailAttachmentId = request.EmailAttachmentId,
                fromAddress = request.FromAddress,
                subject = request.Subject,
                sourceType = request.SourceType,
                sourceName = request.SourceName,
                sourceContentType = EmptyToNull(request.SourceContentType),
                emailContext,
                sourceContent,
                previousExtraction = new
                {
                    errorCode = EmptyToNull(request.PreviousErrorCode),
                    errorMessage = EmptyToNull(request.PreviousErrorMessage),
                    confidence = request.PreviousConfidence,
                },
            },
            JsonOptions
        );

        logger.LogInformation(
            "Enviando correo {EmailMessageId} a AI con {PayloadCharacters} caracteres; fuente {SourceType}.",
            request.EmailMessageId,
            payload.Length,
            request.SourceType
        );

        var grpcRequest = new ExecuteAiStructuredGrpcRequest
        {
            ProfileKey = profileKey,
            CorrelationId = request.CorrelationId,
            RequestHash = ComputeSha256(payload),
            RequestedByName = "DholeDataExtractionService",
            JsonSchemaOverride = PricingEmailJsonSchema,
        };
        grpcRequest.Messages.Add(
            new AiMessageGrpcModel
            {
                Role = "user",
                Content = payload,
            }
        );

        try
        {
            var timeoutSeconds = ReadPositiveInt(
                configuration["AI:EmailFallback:TimeoutSeconds"],
                300
            );

            var response = await client.ExecuteStructuredAsync(
                grpcRequest,
                deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                cancellationToken: cancellationToken
            );

            if (!response.Success)
            {
                return Failure(
                    EmptyToNull(response.ErrorCode) ?? "AI.ExecutionFailed",
                    EmptyToNull(response.ErrorMessage) ?? "AI no pudo analizar el correo."
                );
            }

            if (
                !TryParsePricingEmailResponse(
                    response.JsonContent,
                    out var parsed,
                    out var contractIssue
                )
            )
            {
                logger.LogWarning(
                    "AI devolvió una estructura no reconocible para el correo {EmailMessageId}. "
                        + "Ejecución {AiExecutionId}; detalle {ContractIssue}; hash {ResponseHash}.",
                    request.EmailMessageId,
                    response.ExecutionId,
                    contractIssue,
                    ComputeSha256(response.JsonContent ?? string.Empty)
                );
                logger.LogDebug(
                    "Respuesta estructurada rechazada de AI para el correo {EmailMessageId}: {JsonContent}",
                    request.EmailMessageId,
                    LimitForLog(response.JsonContent)
                );

                return Failure(
                    "AI.InvalidPricingEmailResponse",
                    "AI respondió, pero no fue posible reconocer filas de tarifas en su JSON. "
                        + contractIssue
                );
            }

            var executionId = Guid.TryParse(response.ExecutionId, out var parsedExecutionId)
                ? parsedExecutionId
                : (Guid?)null;
            var rows = parsed?.Rows?
                .Select(ToApplicationRow)
                .Where(HasPricingData)
                .ToArray() ?? [];
            var warnings = parsed?.Warnings?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            var confidence = Math.Clamp(parsed?.Confidence ?? 0m, 0m, 100m);

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
                        ?? "AI no encontró filas de tarifas FCL suficientemente confiables."
                );
            }

            logger.LogInformation(
                "AI analizó el correo {EmailMessageId} con el perfil {ProfileKey}. Ejecución {AiExecutionId}; filas {RowCount}; confianza {Confidence}.",
                request.EmailMessageId,
                profileKey,
                executionId,
                rows.Length,
                confidence
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
            logger.LogWarning(
                exception,
                "AI excedió el timeout al analizar el correo {EmailMessageId}.",
                request.EmailMessageId
            );
            return Failure(
                "AI.Timeout",
                $"AI no respondió dentro de {ReadPositiveInt(configuration["AI:EmailFallback:TimeoutSeconds"], 300)} segundos."
            );
        }
        catch (RpcException exception)
        {
            logger.LogWarning(
                exception,
                "Falló la llamada gRPC a AI para el correo {EmailMessageId}.",
                request.EmailMessageId
            );
            return Failure(
                $"AI.Grpc.{exception.StatusCode}",
                string.IsNullOrWhiteSpace(exception.Status.Detail)
                    ? exception.Message
                    : exception.Status.Detail
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                "AI.Timeout",
                $"AI no respondió dentro de {ReadPositiveInt(configuration["AI:EmailFallback:TimeoutSeconds"], 300)} segundos."
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "No fue posible analizar mediante AI el correo {EmailMessageId}.",
                request.EmailMessageId
            );
            return Failure("AI.Unavailable", exception.Message);
        }
    }

    private static bool TryParsePricingEmailResponse(
        string? content,
        out AiPricingEmailResponse? response,
        out string contractIssue
    )
    {
        response = null;
        contractIssue = string.Empty;

        if (!TryParseJsonRoot(content, out var root, out contractIssue))
        {
            return false;
        }

        root = UnwrapKnownEnvelope(root);
        var rowElements = FindRowElements(root);
        if (rowElements.Count == 0)
        {
            contractIssue = DescribeJsonShape(root);
            return false;
        }

        var rows = rowElements
            .Where(element => element.ValueKind == JsonValueKind.Object)
            .Select(ParsePricingRow)
            .Where(row => row is not null)
            .Cast<AiPricingEmailRowResponse>()
            .Take(200)
            .ToArray();

        if (rows.Length == 0)
        {
            contractIssue = "La colección devuelta por AI no contenía objetos de tarifa.";
            return false;
        }

        var confidence = ReadDecimal(
                root,
                "confidence",
                "confidenceScore",
                "score",
                "confianza"
            ) ?? 0m;
        if (confidence > 0m && confidence <= 1m)
        {
            confidence *= 100m;
        }

        var warnings = ReadStringCollection(
            root,
            "warnings",
            "warning",
            "issues",
            "ambiguities",
            "advertencias"
        );
        var declaredSuccess = ReadBooleanValue(root, "success", "isSuccess", "ok", "successful");
        if (declaredSuccess == false && rows.Length > 0)
        {
            warnings = warnings
                .Append("AI marcó success=false, pero devolvió filas; DataExtraction las validará antes de Pricing.")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        response = new AiPricingEmailResponse(
            rows.Length > 0,
            Math.Clamp(confidence, 0m, 100m),
            rows,
            warnings
        );
        return true;
    }

    private static bool TryParseJsonRoot(
        string? content,
        out JsonElement root,
        out string issue
    )
    {
        root = default;
        issue = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            issue = "AI devolvió una respuesta vacía.";
            return false;
        }

        var candidate = RemoveMarkdownFence(content);
        for (var level = 0; level < 3; level++)
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                var parsed = document.RootElement.Clone();

                if (parsed.ValueKind != JsonValueKind.String)
                {
                    root = parsed;
                    return true;
                }

                var nested = parsed.GetString();
                if (string.IsNullOrWhiteSpace(nested))
                {
                    issue = "AI devolvió una cadena JSON vacía.";
                    return false;
                }

                candidate = RemoveMarkdownFence(nested);
            }
            catch (JsonException exception)
            {
                issue = $"El contenido no es JSON válido: {exception.Message}";
                return false;
            }
        }

        issue = "AI devolvió el JSON codificado como texto demasiadas veces.";
        return false;
    }

    private static JsonElement UnwrapKnownEnvelope(JsonElement root)
    {
        var current = root;

        for (var level = 0; level < 3 && current.ValueKind == JsonValueKind.Object; level++)
        {
            if (HasRowCollection(current) || LooksLikePricingRow(current))
            {
                return current;
            }

            if (
                !TryGetProperty(
                    current,
                    out var nested,
                    "data",
                    "result",
                    "output",
                    "response",
                    "payload",
                    "jsonContent",
                    "content"
                )
            )
            {
                return current;
            }

            if (nested.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                current = nested.Clone();
                continue;
            }

            if (
                nested.ValueKind == JsonValueKind.String
                && TryParseJsonRoot(nested.GetString(), out var parsedNested, out _)
            )
            {
                current = parsedNested;
                continue;
            }

            return current;
        }

        return current;
    }

    private static IReadOnlyList<JsonElement> FindRowElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(element => element.Clone()).ToArray();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<JsonElement>();
        }

        if (
            TryGetProperty(
                root,
                out var rows,
                "rows",
                "rates",
                "tariffs",
                "pricingRows",
                "items",
                "records",
                "results",
                "tarifas"
            )
        )
        {
            if (rows.ValueKind == JsonValueKind.Array)
            {
                return rows.EnumerateArray().Select(element => element.Clone()).ToArray();
            }

            if (rows.ValueKind == JsonValueKind.Object)
            {
                return LooksLikePricingRow(rows) ? [rows.Clone()] : FindRowElements(rows);
            }

            if (
                rows.ValueKind == JsonValueKind.String
                && TryParseJsonRoot(rows.GetString(), out var parsedRows, out _)
            )
            {
                return FindRowElements(parsedRows);
            }
        }

        if (LooksLikePricingRow(root))
        {
            return [root.Clone()];
        }

        if (
            TryGetProperty(root, out var nested, "data", "result", "output", "payload")
            && nested.ValueKind is JsonValueKind.Object or JsonValueKind.Array
        )
        {
            return FindRowElements(nested);
        }

        return Array.Empty<JsonElement>();
    }

    private static AiPricingEmailRowResponse? ParsePricingRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var currency = ReadString(row, "currency", "currencyCode", "moneda")
            ?? InferCurrency(
                row,
                "oceanFreight",
                "freight",
                "oceanRate",
                "oceanFreightRate",
                "rate",
                "flete",
                "amount",
                "totalCost",
                "totalSale"
            );

        return new AiPricingEmailRowResponse(
            ReadString(row, "originPort", "pol", "origin", "portOfLoading", "loadPort", "portOfOrigin", "puertoOrigen"),
            ReadString(row, "portOfExit", "poe", "gateway", "exitPort", "entryPort", "puertoSalida"),
            ReadString(row, "destinationPort", "pod", "destination", "portOfDischarge", "dischargePort", "costaRicaGateway", "puertoDestino"),
            ReadString(row, "containerType", "equipment", "equipmentType", "container", "sizeType", "size", "cntrType", "tipoContenedor"),
            ReadString(row, "carrier", "shippingLine", "line", "naviera", "oceanCarrier"),
            ReadString(row, "agent", "destinationAgent", "agente"),
            ReadString(row, "commodity", "cargo", "product", "mercancia"),
            currency,
            ReadInteger(row, "freeDays", "freeTime", "destinationFreeTime", "diasLibres"),
            ReadInteger(row, "transitDays", "transitTimeDays", "transitTime", "transit", "diasTransito", "tiempoTransito"),
            ReadDate(row, "validFrom", "effectiveDate", "validityFrom", "startDate", "vigenciaDesde", "fechaInicio"),
            ReadDate(row, "validTo", "expirationDate", "expiryDate", "validityTo", "endDate", "vigenciaHasta", "fechaFin"),
            ReadDecimal(row, "oceanFreight", "freight", "oceanRate", "oceanFreightRate", "rate", "flete", "amount", "fleteMaritimo", "fleteMaritimoInternacional"),
            ReadDecimal(row, "originCharges", "originCharge", "chargesAtOrigin"),
            ReadDecimal(row, "destinationCharges", "destinationCharge", "chargesAtDestination"),
            ReadDecimal(row, "surcharges", "surcharge", "additionalCharges"),
            ReadDecimal(row, "totalCost", "cost", "totalCosto"),
            ReadDecimal(row, "totalSale", "sale", "sellingPrice", "totalVenta"),
            ReadDecimal(row, "profit", "utility", "utilidad"),
            ReadDecimal(row, "margin", "marginPercent", "margen"),
            ReadString(row, "spaceComment", "space", "availability", "spaceAvailability"),
            ReadString(row, "remarks", "notes", "comments", "observations", "observaciones")
        );
    }

    private static bool HasRowCollection(JsonElement element)
    {
        return TryGetProperty(
            element,
            out var rows,
            "rows",
            "rates",
            "tariffs",
            "pricingRows",
            "items",
            "records",
            "results",
            "tarifas"
        ) && rows.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.String;
    }

    private static bool LooksLikePricingRow(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return TryGetProperty(
                element,
                out _,
                "originPort",
                "pol",
                "origin",
                "portOfLoading"
            )
            || TryGetProperty(
                element,
                out _,
                "destinationPort",
                "pod",
                "destination",
                "portOfDischarge"
            )
            || TryGetProperty(
                element,
                out _,
                "containerType",
                "equipment",
                "container"
            )
            || TryGetProperty(
                element,
                out _,
                "oceanFreight",
                "freight",
                "oceanRate",
                "rate"
            );
    }

    private static string? ReadString(JsonElement element, params string[] aliases)
    {
        if (!TryGetProperty(element, out var value, aliases))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => EmptyToNull(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static string? InferCurrency(JsonElement element, params string[] aliases)
    {
        if (!TryGetProperty(element, out var value, aliases) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var match = Regex.Match(
            value.GetString() ?? string.Empty,
            @"\b(USD|EUR|CRC|GBP|JPY|CNY|RMB|CAD|MXN|COP|PAB)\b",
            RegexOptions.IgnoreCase
        );
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static bool? ReadBooleanValue(JsonElement element, params string[] aliases)
    {
        if (!TryGetProperty(element, out var value, aliases))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
        {
            return numeric != 0;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()?.Trim();
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            if (int.TryParse(text, out numeric))
            {
                return numeric != 0;
            }
        }

        return null;
    }

    private static int? ReadInteger(JsonElement element, params string[] aliases)
    {
        if (!TryGetProperty(element, out var value, aliases))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var integer))
            {
                return integer;
            }

            if (value.TryGetDecimal(out var number))
            {
                return (int)Math.Round(number, MidpointRounding.AwayFromZero);
            }
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var match = Regex.Match(value.GetString() ?? string.Empty, @"-?\d+");
            if (match.Success && int.TryParse(match.Value, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement element, params string[] aliases)
    {
        if (!TryGetProperty(element, out var value, aliases))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return TryParseFlexibleDecimal(value.GetString(), out var parsed) ? parsed : null;
    }

    private static bool TryParseFlexibleDecimal(string? value, out decimal parsed)
    {
        parsed = 0m;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Regex.Replace(value.Trim(), @"[^0-9,\.\-()]", string.Empty);
        var negative = normalized.StartsWith('(') && normalized.EndsWith(')');
        normalized = normalized.Trim('(', ')');

        var comma = normalized.LastIndexOf(',');
        var dot = normalized.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            var decimalSeparator = comma > dot ? ',' : '.';
            var thousandsSeparator = decimalSeparator == ',' ? "." : ",";
            normalized = normalized.Replace(thousandsSeparator, string.Empty, StringComparison.Ordinal);
            if (decimalSeparator == ',')
            {
                normalized = normalized.Replace(',', '.');
            }
        }
        else if (comma >= 0)
        {
            var decimalDigits = normalized.Length - comma - 1;
            normalized = decimalDigits is 1 or 2
                ? normalized.Replace(',', '.')
                : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (dot >= 0)
        {
            var dotCount = normalized.Count(character => character == '.');
            if (dotCount > 1)
            {
                var lastDot = normalized.LastIndexOf('.');
                normalized = normalized[..lastDot].Replace(".", string.Empty, StringComparison.Ordinal)
                    + normalized[lastDot..];
            }
            else if (normalized.Length - dot - 1 == 3)
            {
                normalized = normalized.Replace(".", string.Empty, StringComparison.Ordinal);
            }
        }

        if (
            !decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out parsed
            )
        )
        {
            return false;
        }

        if (negative)
        {
            parsed = -parsed;
        }

        return true;
    }

    private static DateTime? ReadDate(JsonElement element, params string[] aliases)
    {
        var value = ReadString(element, aliases);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats =
        [
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyyMMdd",
            "dd/MM/yyyy",
            "MM/dd/yyyy",
            "dd-MM-yyyy",
            "MM-dd-yyyy",
            "dd-MMM-yyyy",
            "d-MMM-yyyy",
            "MMM d yyyy",
            "d MMM yyyy",
        ];

        if (
            DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var exact
            )
            || DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out exact
            )
        )
        {
            return exact.Date;
        }

        return null;
    }

    private static string[] ReadStringCollection(JsonElement element, params string[] aliases)
    {
        if (!TryGetProperty(element, out var value, aliases))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = EmptyToNull(value.GetString());
            return text is null ? [] : [text];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? EmptyToNull(item.GetString()) : null)
            .Where(item => item is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetProperty(
        JsonElement element,
        out JsonElement value,
        params string[] aliases
    )
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var normalizedAliases = aliases
            .Select(NormalizePropertyName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (normalizedAliases.Contains(NormalizePropertyName(property.Name)))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string NormalizePropertyName(string value)
    {
        return new string(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray()
        );
    }

    private static string DescribeJsonShape(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            var properties = root
                .EnumerateObject()
                .Select(property => property.Name)
                .Take(12)
                .ToArray();
            return properties.Length == 0
                ? "AI devolvió un objeto JSON vacío."
                : $"No se encontró rows/rates/tariffs. Propiedades detectadas: {string.Join(", ", properties)}.";
        }

        return $"La raíz JSON fue {root.ValueKind}, no un objeto o arreglo de tarifas.";
    }

    private static string RemoveMarkdownFence(string content)
    {
        var value = content.Trim();
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var firstLineBreak = value.IndexOf('\n');
        if (firstLineBreak >= 0)
        {
            value = value[(firstLineBreak + 1)..];
        }

        if (value.EndsWith("```", StringComparison.Ordinal))
        {
            value = value[..^3];
        }

        return value.Trim();
    }

    private static string LimitForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<vacío>";
        }

        const int maximum = 4_000;
        return value.Length <= maximum ? value : value[..maximum] + "...[truncado]";
    }

    private static AiPricingEmailRow ToApplicationRow(AiPricingEmailRowResponse row)
    {
        return new AiPricingEmailRow(
            EmptyToNull(row.OriginPort),
            EmptyToNull(row.PortOfExit),
            EmptyToNull(row.DestinationPort),
            EmptyToNull(row.ContainerType),
            EmptyToNull(row.Carrier),
            EmptyToNull(row.Agent),
            EmptyToNull(row.Commodity),
            EmptyToNull(row.Currency)?.ToUpperInvariant(),
            row.FreeDays,
            row.TransitDays,
            row.ValidFrom,
            row.ValidTo,
            row.OceanFreight,
            row.OriginCharges,
            row.DestinationCharges,
            row.Surcharges,
            row.TotalCost,
            row.TotalSale,
            row.Profit,
            row.Margin,
            EmptyToNull(row.SpaceComment),
            EmptyToNull(row.Remarks)
        );
    }

    private static bool HasPricingData(AiPricingEmailRow row)
    {
        return !string.IsNullOrWhiteSpace(row.OriginPort)
            || !string.IsNullOrWhiteSpace(row.DestinationPort)
            || !string.IsNullOrWhiteSpace(row.ContainerType)
            || !string.IsNullOrWhiteSpace(row.Carrier)
            || row.OceanFreight.HasValue
            || row.TotalCost.HasValue
            || row.TotalSale.HasValue;
    }

    private static AiPricingEmailAnalysisResult Failure(string errorCode, string errorMessage)
    {
        return new AiPricingEmailAnalysisResult(
            false,
            null,
            0m,
            Array.Empty<AiPricingEmailRow>(),
            Array.Empty<string>(),
            errorCode,
            errorMessage
        );
    }


    private string? LimitText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var maximumCharacters = ReadPositiveInt(
            configuration["AI:EmailFallback:MaximumContentCharacters"],
            50_000
        );

        return value.Length <= maximumCharacters
            ? value
            : value[..maximumCharacters] + "\n[CONTENIDO TRUNCADO POR LÍMITE]";
    }

    private string? BuildEmailContext(string? bodyText, string? bodyHtml)
    {
        var value = !string.IsNullOrWhiteSpace(bodyText)
            ? bodyText
            : StripHtml(bodyHtml);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var maximumCharacters = ReadPositiveInt(
            configuration["AI:EmailFallback:MaximumEmailContextCharacters"],
            8_000
        );
        var normalized = Regex.Replace(value, @"[ \t]+", " ").Trim();
        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..maximumCharacters] + "\n[CONTEXTO DEL CORREO TRUNCADO]";
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var value = Regex.Replace(
            html,
            "<(script|style)[^>]*>.*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        value = Regex.Replace(value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "</(p|div|tr|li|h[1-6])>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "</(td|th)>", "\t", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(value);
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private sealed record AiPricingEmailResponse(
        bool Success,
        decimal Confidence,
        AiPricingEmailRowResponse[]? Rows,
        string[]? Warnings
    );

    private sealed record AiPricingEmailRowResponse(
        string? OriginPort,
        string? PortOfExit,
        string? DestinationPort,
        string? ContainerType,
        string? Carrier,
        string? Agent,
        string? Commodity,
        string? Currency,
        int? FreeDays,
        int? TransitDays,
        DateTime? ValidFrom,
        DateTime? ValidTo,
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
