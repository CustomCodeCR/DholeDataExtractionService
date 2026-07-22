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
        var keywordHits = RateKeywords.Count(keyword =>
            text.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        );
        var hasRateColumnSignals = text.Contains("POL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("POD", StringComparison.OrdinalIgnoreCase)
            || text.Contains("POE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Ocean Freight", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Flete", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Container", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Contenedor", StringComparison.OrdinalIgnoreCase);
        var hasBodyContent = !string.IsNullOrWhiteSpace(plainBody);
        var hasTableStructure = HasHtmlTable(message.BodyHtml)
            || HasDelimitedTextTable(message.BodyText);
        var hasTableSignals = hasTableStructure && hasRateColumnSignals;

        var bodyConfidence = Math.Min(85m, keywordHits * 8m + (hasTableSignals ? 25m : 0m));
        var attachmentConfidence = supportedAttachments.Length > 0
            ? 75m
            : aiReadableAttachments.Length > 0
                ? 55m
                : 0m;
        var confidence = Math.Clamp(Math.Max(bodyConfidence, attachmentConfidence), 0m, 100m);
        var hasProcessableAttachments = attachmentsToProcess.Length > 0;

        // El cuerpo solo se procesa cuando conserva una estructura tabular real.
        // La IA es un fallback para tarifarios tabulares que DataExtraction no comprende,
        // no para convertir correos redactados libremente en tarifas.
        var processBody = hasBodyContent
            && hasTableStructure
            && (hasProcessableAttachments
                ? account.ProcessBodyEvenWithAttachments
                : account.ProcessBodyWhenNoSupportedAttachments);
        var containsRates = hasProcessableAttachments || processBody;

        var reason = containsRates
            ? $"Adjuntos nativos: {supportedAttachments.Length}; adjuntos para fallback AI: {aiReadableAttachments.Length}; tabla en cuerpo: {hasTableStructure}; coincidencias tarifarias: {keywordHits}."
            : hasBodyContent && !hasTableStructure
                ? "El cuerpo del correo no contiene una tabla tarifaria procesable."
                : hasBodyContent
                    ? "El correo contiene una tabla, pero la cuenta tiene deshabilitado el procesamiento del cuerpo."
                    : "El correo no contiene una tabla ni adjuntos procesables.";

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
                && !ReviewablePricingIssueCodes.Contains(x.Code)
                && x.ExtractedPricingRowId.HasValue
            )
            .Select(x => x.ExtractedPricingRowId!.Value)
            .Distinct()
            .Count();
        var hasGlobalBlockingIssue = response.Issues.Any(x =>
            x.IsBlocking
            && !ReviewablePricingIssueCodes.Contains(x.Code)
            && !x.ExtractedPricingRowId.HasValue
        );
        if (hasGlobalBlockingIssue)
        {
            return 0m;
        }

        var reviewRows = response.Issues
            .Where(x =>
                (!x.IsBlocking || ReviewablePricingIssueCodes.Contains(x.Code))
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


    private static bool IsNativeDataExtractionAttachment(EmailAttachment attachment)
    {
        return attachment.SourceFileType
            is SourceFileType.Excel
                or SourceFileType.Csv
                or SourceFileType.Pdf
                or SourceFileType.Email;
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
}
