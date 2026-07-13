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
        "missing_port_of_exit",
        "missing_agent",
        "unknown_origin_port",
        "unknown_port_of_exit",
        "unknown_destination_port",
        "unknown_container_type",
        "unknown_carrier",
        "unknown_agent",
        "unknown_currency",
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
        var supportedAttachments = attachments
            .Where(x => x.SourceFileType is SourceFileType.Excel or SourceFileType.Csv or SourceFileType.Pdf or SourceFileType.Email)
            .Where(x => x.SizeBytes > 0)
            .Select(x => x.Id)
            .ToArray();

        var text = $"{message.Subject}\n{message.BodyText}\n{StripHtml(message.BodyHtml)}";
        var keywordHits = RateKeywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        var hasTableSignals = text.Contains("POL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("POD", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Ocean Freight", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Flete", StringComparison.OrdinalIgnoreCase);

        var bodyConfidence = Math.Min(85m, keywordHits * 8m + (hasTableSignals ? 25m : 0m));
        var attachmentConfidence = supportedAttachments.Length > 0 ? 75m : 0m;
        var confidence = Math.Clamp(Math.Max(bodyConfidence, attachmentConfidence), 0m, 100m);
        var processBody = account.ProcessBodyEvenWithAttachments
            || (account.ProcessBodyWhenNoSupportedAttachments && supportedAttachments.Length == 0 && bodyConfidence >= 30m)
            || bodyConfidence >= 70m;

        var containsRates = confidence >= 30m || supportedAttachments.Length > 0;
        var reason = containsRates
            ? $"Adjuntos soportados: {supportedAttachments.Length}. Coincidencias en cuerpo/asunto: {keywordHits}."
            : "No se detectaron palabras o adjuntos relacionados con tarifas.";

        return new EmailClassificationResult(
            containsRates,
            processBody,
            supportedAttachments,
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

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return Regex.Replace(html, "<[^>]+>", " ");
    }
}
