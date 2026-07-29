using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Contracts.Extraction;
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
            .Where(IsNativeDataExtractionAttachment)
            .ToArray();
        var aiReadableAttachments = nonEmptyAttachments
            .Where(x => !IsNativeDataExtractionAttachment(x) && IsAiReadableDocument(x))
            .ToArray();
        var attachmentsToProcess = supportedAttachments
            .Concat(aiReadableAttachments)
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
        var hasRateSignals = (
                hasRateColumnSignals
                && (keywordHits >= 2 || hasAmountSignal)
            )
            || (keywordHits >= 3 && hasAmountSignal);

        var bodyConfidence = Math.Min(
            85m,
            keywordHits * 8m
                + (hasRateSignals ? 15m : 0m)
                + (hasTableSignals ? 10m : 0m)
        );
        var attachmentConfidence = supportedAttachments.Length > 0
            ? 75m
            : aiReadableAttachments.Length > 0
                ? 55m
                : 0m;
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
            ? $"Adjuntos nativos: {supportedAttachments.Length}; adjuntos legibles por AI: {aiReadableAttachments.Length}; cuerpo tarifario: {processBody}; tabla detectada: {hasTableStructure}; coincidencias tarifarias: {keywordHits}."
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

        return Math.Clamp(usableRatio * 100m + attachmentBonus - reviewPenalty - bodyPenalty, 0m, 100m);
    }


    private static bool IsReviewablePricingIssue(string code)
    {
        return ReviewablePricingIssueCodes.Contains(code)
            || code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase);
    }


    private static bool IsNativeDataExtractionAttachment(EmailAttachment attachment)
    {
        return attachment.SourceFileType
            is SourceFileType.Excel
                or SourceFileType.Csv
                or SourceFileType.Pdf
                or SourceFileType.Email
                or SourceFileType.Image;
    }

    private static bool IsAiReadableDocument(EmailAttachment attachment)
    {
        var extension = attachment.FileExtension?.Trim().ToLowerInvariant();
        if (
            extension
            is ".docx"
                or ".rtf"
                or ".json"
                or ".xml"
                or ".md"
                or ".tsv"
                or ".log"
        )
        {
            return true;
        }

        var contentType = attachment.ContentType;
        return !string.IsNullOrWhiteSpace(contentType)
            && (
                contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("application/xml", StringComparison.OrdinalIgnoreCase)
            );
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

        return tabularRows >= 2;
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
