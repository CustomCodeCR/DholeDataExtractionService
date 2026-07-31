using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Infrastructure.Email;

public sealed class EmailRateClassifier : IEmailRateClassifier
{
    private static readonly HashSet<string> ReviewablePricingIssueCodes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "missing_agent",
        "unknown_agent",
        "expired_rate",
    };

    private static readonly string[] RateKeywords =
    [
        "tarifa", "tarifas", "flete", "fletes", "cotizacion", "cotización", "naviera", "carrier",
        "freight", "ocean freight", "rate", "rates", "surcharge", "validity", "vigencia",
        "pol", "pod", "poe", "container", "contenedor", "20gp", "40hc", "40gp"
    ];

    public EmailClassificationResult Classify(
        EmailMessage message,
        IReadOnlyCollection<EmailAttachment> attachments,
        EmailIngestionAccount account
    )
    {
        var nonEmptyAttachments = attachments.Where(x => x.SizeBytes > 0).ToArray();
        var supportedAttachments = nonEmptyAttachments
            .Where(EmailAttachmentExtractionPolicy.IsSupported)
            .ToArray();
        var attachmentsToProcess = supportedAttachments
            .Select(x => x.Id)
            .Distinct()
            .ToArray();

        var plainBody = string.Join(
            "\n",
            new[] { message.BodyText, StripHtml(message.BodyHtml) }
                .Where(value => !string.IsNullOrWhiteSpace(value))
        );
        var text = $"{message.Subject}\n{plainBody}";
        var keywordHits = RateKeywords.Count(keyword => ContainsKeyword(text, keyword));
        var hasRateColumnSignals = text.Contains("POL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("POD", StringComparison.OrdinalIgnoreCase)
            || text.Contains("POE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Ocean Freight", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Flete", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Container", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Contenedor", StringComparison.OrdinalIgnoreCase)
            || text.Contains("20'", StringComparison.OrdinalIgnoreCase)
            || text.Contains("20’", StringComparison.OrdinalIgnoreCase)
            || text.Contains("40HC", StringComparison.OrdinalIgnoreCase)
            || text.Contains("40HQ", StringComparison.OrdinalIgnoreCase);
        var hasAmountSignal = Regex.IsMatch(
            text,
            @"(?:USD|EUR|CRC|\$|€|₡)\s*\d|\b\d{1,3}(?:[.,]\d{3})+(?:[.,]\d+)?\b",
            RegexOptions.IgnoreCase
        );
        var hasBodyContent = !string.IsNullOrWhiteSpace(plainBody);
        var hasTableStructure = HasHtmlTable(message.BodyHtml)
            || HasDelimitedTextTable(message.BodyText);
        var hasTableSignals = hasTableStructure && hasRateColumnSignals;
        var hasNarrativeNacSignals = Regex.IsMatch(
            text,
            @"\b(?:pls|please)\s+consider\s+rate\b",
            RegexOptions.IgnoreCase
        )
            && Regex.IsMatch(text, @"\bvalid\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"\bCarrier\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"\bPOL\s*:", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"\bPOD\s*:", RegexOptions.IgnoreCase)
            && hasAmountSignal;
        var hasRateSignals = hasNarrativeNacSignals
            || (
                hasRateColumnSignals
                && (keywordHits >= 2 || hasAmountSignal)
            )
            || (keywordHits >= 3 && hasAmountSignal);

        var bodyConfidence = Math.Min(
            90m,
            keywordHits * 8m
                + (hasRateSignals ? 15m : 0m)
                + (hasTableSignals ? 10m : 0m)
                + (hasNarrativeNacSignals ? 25m : 0m)
        );
        var attachmentConfidence = supportedAttachments.Length > 0 ? 75m : 0m;
        var confidence = Math.Clamp(Math.Max(bodyConfidence, attachmentConfidence), 0m, 100m);
        var hasProcessableAttachments = attachmentsToProcess.Length > 0;

        // El cuerpo puede venir como HTML, tabla pegada, texto alineado o texto corrido.
        // La estructura no es un requisito de encolamiento: la estrategia automática
        // decidirá entre el extractor determinístico y AI, y siempre validará en Config.
        var processBody = hasBodyContent
            && hasRateSignals
            && (hasProcessableAttachments
                ? account.ProcessBodyEvenWithAttachments
                : account.ProcessBodyWhenNoSupportedAttachments);
        var containsRates = hasProcessableAttachments || processBody;

        var reason = containsRates
            ? $"Adjuntos soportados (PDF/CSV/XLSX): {supportedAttachments.Length}; cuerpo tarifario: {processBody}; tabla detectada: {hasTableStructure}; coincidencias tarifarias: {keywordHits}."
            : hasBodyContent && !hasRateSignals
                ? "El cuerpo del correo no contiene señales suficientes de una tarifa."
                : hasBodyContent
                    ? "El correo contiene datos tarifarios, pero la cuenta tiene deshabilitado el procesamiento del cuerpo."
                    : "El correo no contiene cuerpo tarifario ni adjuntos procesables.";

        return new EmailClassificationResult(
            containsRates,
            processBody,
            attachmentsToProcess,
            confidence,
            reason
        );
    }

    public decimal CalculateExtractionConfidence(
        ExtractPricingDataResponse response,
        EmailMessage message,
        EmailAttachment? attachment
    )
    {
        if (!response.Success || response.Summary.TotalRows <= 0)
        {
            return 0m;
        }

        var totalRows = response.Summary.TotalRows;
        var hardBlockingRows = response.Issues
            .Where(x =>
                x.IsBlocking
                && !IsReviewablePricingIssue(x.Code)
                && x.ExtractedPricingRowId.HasValue
            )
            .Select(x => x.ExtractedPricingRowId!.Value)
            .Distinct()
            .Count();
        var hasGlobalBlockingIssue = response.Issues.Any(x =>
            x.IsBlocking
            && !IsReviewablePricingIssue(x.Code)
            && !x.ExtractedPricingRowId.HasValue
        );
        if (hasGlobalBlockingIssue)
        {
            return 0m;
        }

        var reviewRows = response.Issues
            .Where(x =>
                (!x.IsBlocking || IsReviewablePricingIssue(x.Code))
                && x.ExtractedPricingRowId.HasValue
            )
            .Select(x => x.ExtractedPricingRowId!.Value)
            .Distinct()
            .Count();
        var usableRatio = decimal.Divide(totalRows - hardBlockingRows, totalRows);
        var reviewPenalty = decimal.Divide(reviewRows, totalRows) * 5m;
        var attachmentBonus = attachment is not null && attachment.SourceFileType is SourceFileType.Excel or SourceFileType.Csv ? 10m : 0m;
        var bodyPenalty = attachment is null ? 5m : 0m;
        var structuralConfidence = Math.Clamp(
            usableRatio * 100m + attachmentBonus - reviewPenalty - bodyPenalty,
            0m,
            100m
        );
        var normalizationConfidence = CalculateCatalogNormalizationConfidence(
            response
        );

        // El porcentaje usado para decidir si AI interviene mide tanto la estructura
        // extraída como la normalización real contra Config. Dos catálogos requeridos
        // sin resolver dejan la fila por debajo del umbral de 75%.
        return Math.Min(structuralConfidence, normalizationConfidence);
    }

    private static decimal CalculateCatalogNormalizationConfidence(
        ExtractPricingDataResponse response
    )
    {
        if (response.Rows.Count == 0)
        {
            return 0m;
        }

        decimal accumulated = 0m;
        foreach (var row in response.Rows)
        {
            var expected = 5m;
            var matched = 0m;

            matched += row.OriginPortReference is not null ? 1m : 0m;
            matched += row.PortOfExitReference is not null ? 1m : 0m;
            matched += row.ContainerTypeReference is not null ? 1m : 0m;
            matched += row.CarrierReference is not null ? 1m : 0m;
            matched += row.CurrencyReference is not null ? 1m : 0m;

            if (!string.IsNullOrWhiteSpace(row.DestinationPort))
            {
                expected++;
                matched += row.DestinationPortReference is not null ? 1m : 0m;
            }

            if (!string.IsNullOrWhiteSpace(row.Agent))
            {
                expected++;
                matched += row.AgentReference is not null ? 1m : 0m;
            }

            accumulated += decimal.Divide(matched, expected) * 100m;
        }

        return Math.Clamp(
            decimal.Divide(accumulated, response.Rows.Count),
            0m,
            100m
        );
    }


    private static bool IsReviewablePricingIssue(string code)
    {
        return ReviewablePricingIssueCodes.Contains(code)
            || code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase);
    }


    private static bool HasHtmlTable(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        return html.Contains("<table", StringComparison.OrdinalIgnoreCase)
            && html.Contains("<tr", StringComparison.OrdinalIgnoreCase)
            && (
                html.Contains("<td", StringComparison.OrdinalIgnoreCase)
                || html.Contains("<th", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static bool HasDelimitedTextTable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var rows = text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();

        var tabularRows = rows.Count(value =>
            value.Count(character => character == '\t') >= 2
            || value.Count(character => character == '|') >= 2
        );

        if (tabularRows >= 2)
        {
            return true;
        }

        // Outlook can flatten an HTML table into one cell per line. A sequence
        // POL/POD/CARRIER followed by equipment columns is still a real table.
        for (var index = 0; index < rows.Length; index++)
        {
            if (!NormalizeHeaderToken(rows[index]).Equals("pol", StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = rows
                .Skip(index)
                .Take(12)
                .Select(NormalizeHeaderToken)
                .ToArray();

            if (
                candidate.Contains("pod", StringComparer.OrdinalIgnoreCase)
                && candidate.Contains("carrier", StringComparer.OrdinalIgnoreCase)
                && candidate.Any(value => Regex.IsMatch(value, @"^(20|40|45)(gp|hq|hc|dv|dc|nor)?$"))
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeHeaderToken(string value)
    {
        return Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return Regex.Replace(html, "<[^>]+>", " ");
    }

    private static bool ContainsKeyword(string text, string keyword)
    {
        return Regex.IsMatch(
            text,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(keyword)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
    }
}
