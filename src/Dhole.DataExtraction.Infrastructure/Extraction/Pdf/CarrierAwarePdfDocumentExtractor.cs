using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

/// <summary>
/// Treats a carrier name in the attachment filename as stronger evidence than footer/legal
/// boilerplate inside the PDF. This is important for agency templates that reuse text from
/// another shipping line even though the visible tariff belongs to YML/Yang Ming, PIL, etc.
/// </summary>
public sealed class CarrierAwarePdfDocumentExtractor(
    PdfDocumentExtractor innerExtractor
) : IDocumentExtractor
{
    public SourceFileType FileType => SourceFileType.Pdf;

    public async Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    )
    {
        var document = await innerExtractor.ExtractAsync(input, cancellationToken);
        var fileCarrier = InferCarrierFromFileName(input.OriginalFileName);
        if (string.IsNullOrWhiteSpace(fileCarrier))
        {
            return document;
        }

        var changed = false;
        var tables = document.Tables.Select(table =>
        {
            var rows = table.Rows.Select(row =>
            {
                var carrierKey = row.Values.Keys.FirstOrDefault(IsCarrierColumn);
                if (carrierKey is null)
                {
                    return row;
                }

                var values = new Dictionary<string, string?>(
                    row.Values,
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    [carrierKey] = fileCarrier,
                };
                changed = true;
                return new ExtractedRow(
                    row.RowNumber,
                    values,
                    JsonSerializer.Serialize(values)
                );
            }).ToArray();

            return new ExtractedTable(table.SheetName, table.Headers, rows);
        }).ToArray();

        return changed
            ? document with { Tables = tables }
            : document;
    }

    private static bool IsCarrierColumn(string value)
    {
        var normalized = Regex.Replace(value, @"[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase)
            .ToLowerInvariant();
        return normalized is "carrier" or "naviera" or "shippingline";
    }

    internal static string? InferCarrierFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var candidates = new (string Pattern, string Carrier)[]
        {
            (@"\b(?:YANG|YAN)\s*MING\b|\bYML\b", "YANG MING"),
            (@"\bPILL?\b", "PIL"),
            (@"\bCMA\s*CGM\b", "CMA CGM"),
            (@"\bHAPAG(?:-|\s*)LLOYD\b", "HAPAG-LLOYD"),
            (@"\bEVERGREEN\b", "EVERGREEN"),
            (@"\bMAERSK\b", "MAERSK"),
            (@"\bCOSCO\b", "COSCO"),
            (@"\bOOCL\b", "OOCL"),
            (@"\bMSC\b", "MSC"),
            (@"\bWHL\b", "WHL"),
            (@"\bONE\b", "ONE"),
        };

        var matches = candidates
            .Where(candidate => Regex.IsMatch(fileName, candidate.Pattern, RegexOptions.IgnoreCase))
            .Select(candidate => candidate.Carrier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }
}
