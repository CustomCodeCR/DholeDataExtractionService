using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Email;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Infrastructure.Mapping;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Email;

public sealed class EmailDocumentExtractor : IDocumentExtractor
{
    public SourceFileType FileType => SourceFileType.Email;

    public static ExtractedTable? TryExtractNarrativeNacTable(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var plainText = EmailPricingContentSelector.SelectNewestPricingSection(
            NormalizeEmailBody(ExtractBody(source))
        );
        var lines = plainText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeLine)
            .Select(x => x.Trim().TrimStart('>', '|', '-', '*').Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return TryParseNarrativeNacRates(lines).FirstOrDefault();
    }

    public Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    )
    {
        var plainText = IsRawEmail(input)
            ? ReadRawEmail(input.FileContent)
            : EmailPricingContentSelector.SelectNewestPricingSection(
                NormalizeEmailBody(ExtractBody(DecodeText(input.FileContent)))
            );

        var lines = plainText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeLine)
            .Select(x => x.Trim().TrimStart('>', '|', '-', '*').Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        // Some carriers send NAC/contract rates as prose followed by repeated
        // POL/POD/COMM blocks instead of a table. Parse the newest narrative offer
        // first and stop before the quoted email history.
        var tables = TryParseNarrativeNacRates(lines);

        // Outlook and several freight forwarders flatten copied HTML tables into
        // one cell per line. Parse that FCL cell stream before attempting the
        // traditional delimiter/key-value strategies. Only the first valid table
        // is used so quoted historical rate tables are not imported again.
        if (tables.Count == 0)
        {
            tables = TryParseStackedFclTables(lines);
        }

        if (tables.Count == 0)
        {
            tables.AddRange(TryParseDelimitedTables(lines));
        }

        if (tables.Count == 0)
        {
            tables.AddRange(TryBuildMultiRowKeyValueTables(lines));
        }

        if (tables.Count == 0)
        {
            var keyValueTable = TryBuildSingleKeyValueTable(plainText);
            if (keyValueTable is not null)
            {
                tables.Add(keyValueTable);
            }
        }

        if (tables.Count == 0)
        {
            tables.Add(new ExtractedTable("EMAIL", [], []));
        }

        return Task.FromResult(
            new ExtractedDocument(input.OriginalFileName, SourceFileType.Email, tables, plainText)
        );
    }


    private static bool IsRawEmail(DocumentExtractionInput input)
    {
        return input.FileExtension?.Equals(".eml", StringComparison.OrdinalIgnoreCase) == true
            || input.ContentType?.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string ReadRawEmail(byte[] content)
    {
        try
        {
            var message = SimpleMimeParser.ParseRawMessage(content, "document-extractor", null);
            return EmailPricingContentSelector.SelectPreferredBody(
                message.BodyText,
                message.BodyHtml
            );
        }
        catch
        {
            var fallback = SimpleMimeParser.ParseRawMessageFallback(
                content,
                "document-extractor-fallback",
                null
            );
            return EmailPricingContentSelector.SelectPreferredBody(
                fallback.BodyText,
                fallback.BodyHtml
            );
        }
    }

    private static string DecodeText(byte[] content)
    {
        return TextContentDecoder.Decode(content);
    }

    private static string ExtractBody(string text)
    {
        var normalized = text.Replace("=\r\n", string.Empty).Replace("=\n", string.Empty);
        var headerEnd = normalized.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd >= 0 && LooksLikeEmailHeaders(normalized[..headerEnd]))
        {
            return normalized[(headerEnd + 4)..];
        }

        headerEnd = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        if (headerEnd >= 0 && LooksLikeEmailHeaders(normalized[..headerEnd]))
        {
            return normalized[(headerEnd + 2)..];
        }

        return normalized;
    }

    private static bool LooksLikeEmailHeaders(string text)
    {
        return text.Contains("From:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Subject:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Content-Type:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MIME-Version:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEmailBody(string text)
    {
        var decoded = WebUtility.HtmlDecode(text)
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);

        decoded = Regex.Replace(decoded, @"<\s*style[^>]*>.*?<\s*/\s*style\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        decoded = Regex.Replace(decoded, @"<\s*script[^>]*>.*?<\s*/\s*script\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        decoded = Regex.Replace(decoded, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<\s*/\s*(div|p|li|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<\s*(div|p|li|h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<\s*/?\s*tr[^>]*>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<\s*/?\s*t[dh][^>]*>", "|", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<[^>]+>", " ", RegexOptions.IgnoreCase);
        // Preserve tabs and repeated spaces because plain-text email clients use them
        // as table column separators. Collapsing them makes valid FCL tables unreadable.
        decoded = Regex.Replace(decoded, @"[ \t]+(?=\r?$)", string.Empty, RegexOptions.Multiline);
        decoded = Regex.Replace(decoded, @"\n{2,}", "\n");

        return decoded.Trim();
    }


    private static List<ExtractedTable> TryParseNarrativeNacRates(
        IReadOnlyCollection<string> lines
    )
    {
        var source = lines.ToArray();
        var rateLineIndex = Array.FindIndex(
            source,
            line => Regex.IsMatch(
                line,
                @"\b(?:pls|please)\s+consider\s+rate\b",
                RegexOptions.IgnoreCase
            )
        );

        if (rateLineIndex < 0)
        {
            return [];
        }

        var rateLine = source[rateLineIndex];
        var offer = TryParseNarrativeRateOffer(rateLine, source, rateLineIndex);
        if (offer is null)
        {
            return [];
        }

        var currentMessageEnd = FindNarrativeMessageEnd(source, rateLineIndex + 1);
        var currentMessageLines = source
            .Skip(rateLineIndex + 1)
            .Take(Math.Max(0, currentMessageEnd - rateLineIndex - 1))
            .ToArray();
        var groups = ParseNarrativeRouteGroups(currentMessageLines);
        if (groups.Count == 0)
        {
            return [];
        }

        var surcharges = currentMessageLines
            .FirstOrDefault(line => line.StartsWith("Subject to", StringComparison.OrdinalIgnoreCase));
        var rows = new List<ExtractedRow>();
        var rowNumber = 1;

        foreach (var carrierRate in offer.CarrierRates)
        {
            var carrier = carrierRate.Carrier;
            var isOneNac = carrier.Equals("ONE", StringComparison.OrdinalIgnoreCase);
            IReadOnlyList<NarrativeRouteGroup> carrierGroups = isOneNac
                ? groups
                : [MergeNarrativeGroups(groups, offer.ExcludedOrigins)];

            foreach (var group in carrierGroups)
            {
                var remarks = BuildNarrativeRemarks(
                    carrier,
                    group,
                    offer,
                    surcharges,
                    isOneNac
                );
                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Carrier"] = carrier,
                    ["POL"] = group.Origins,
                    // The source label is POD, but in these ocean-rate emails it
                    // means Port of Discharge. Dhole stores it as POE.
                    ["POE"] = group.PortsOfDischarge,
                    ["ContainerSize"] = offer.ContainerType,
                    ["Commodity"] = isOneNac ? group.Commodity : null,
                    ["Currency"] = offer.Currency,
                    ["FreightAmount"] = carrierRate.Amount,
                    ["OriginCharges"] = group.OriginCharge,
                    ["Surcharges"] = ParseNarrativePerContainerSurcharges(surcharges),
                    ["ValidFrom"] = offer.ValidFrom,
                    ["ValidTo"] = offer.ValidTo,
                    ["FreeDays"] = offer.FreeDays,
                    ["Remarks"] = remarks,
                };

                rows.Add(new ExtractedRow(rowNumber++, values, JsonSerializer.Serialize(values)));
            }
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var headers = rows
            .SelectMany(row => row.Values.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return [new ExtractedTable("EMAIL NAC Narrative", headers, rows)];
    }

    private static NarrativeRateOffer? TryParseNarrativeRateOffer(
        string rateLine,
        IReadOnlyList<string> source,
        int rateLineIndex
    )
    {
        var validityMatch = Regex.Match(
            rateLine,
            @"\bvalid\s+(?<value>.+?)(?=\s+(?:Carrier|with)\b|\s*,\s*(?:Carrier|with)\b|$)",
            RegexOptions.IgnoreCase
        );
        var freeDaysMatch = Regex.Match(
            rateLine,
            @"\b(?<days>\d{1,3})\s*days?\s+free\b",
            RegexOptions.IgnoreCase
        );
        if (!validityMatch.Success || !freeDaysMatch.Success)
        {
            return null;
        }

        var validity = CleanNarrativeValidity(validityMatch.Groups["value"].Value);
        var (validFrom, validTo) = SplitNarrativeValidity(validity);
        if (string.IsNullOrWhiteSpace(validFrom) || string.IsNullOrWhiteSpace(validTo))
        {
            return null;
        }

        var prefix = rateLine[..validityMatch.Index];
        var ratePart = Regex.Replace(
            prefix,
            @"^.*?\b(?:pls|please)\s+consider\s+rate\s+",
            string.Empty,
            RegexOptions.IgnoreCase
        ).Trim(' ', ',', '.', ';');
        var carrierClause = Regex.Match(
            rateLine,
            @"\bCarrier\s+(?<value>.+?)(?=\s+with\b|\s*,|$)",
            RegexOptions.IgnoreCase
        );

        var carrierRates = ParseCarrierRates(ratePart, carrierClause.Success
            ? carrierClause.Groups["value"].Value
            : null);
        if (carrierRates.Count == 0)
        {
            return null;
        }

        var containerType = InferNarrativeContainerType(rateLine, source, rateLineIndex);
        if (string.IsNullOrWhiteSpace(containerType))
        {
            // This WWL contract thread consistently quotes the paired NAC rate per
            // high-cube container. Keep the inference explicit in Remarks.
            containerType = "40HC";
        }

        var exclusionsMatch = Regex.Match(
            rateLine,
            @"\bexcept\s+(?<value>[^)]+)",
            RegexOptions.IgnoreCase
        );
        var exclusions = exclusionsMatch.Success
            ? SplitNarrativeRouteValue(exclusionsMatch.Groups["value"].Value)
                .Select(NormalizeNarrativePortToken)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return new NarrativeRateOffer(
            carrierRates,
            "USD",
            containerType,
            validFrom,
            validTo,
            freeDaysMatch.Groups["days"].Value,
            exclusions,
            rateLine
        );
    }

    private static IReadOnlyList<NarrativeCarrierRate> ParseCarrierRates(
        string ratePart,
        string? carrierClause
    )
    {
        var explicitCarrierAmounts = Regex.Matches(
            ratePart,
            @"(?<carrier>MSC|ONE|MSK|MAERSK|HPL|PIL|COSCO|CMA(?:\s*CGM)?|OOCL|WHL)\s*(?:NAC\s*)?(?:USD|US\$|\$)\s*(?<amount>\d[\d,]*(?:\.\d+)?)",
            RegexOptions.IgnoreCase
        );
        if (explicitCarrierAmounts.Count > 0)
        {
            return explicitCarrierAmounts
                .Select(match => new NarrativeCarrierRate(
                    NormalizeNarrativeCarrier(match.Groups["carrier"].Value),
                    match.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal)
                ))
                .GroupBy(item => item.Carrier, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(carrierClause))
        {
            return [];
        }

        var carriers = Regex.Split(
                Regex.Replace(carrierClause, @"\bNAC\b", string.Empty, RegexOptions.IgnoreCase),
                @"\s*(?:/|,|\band\b|\by\b)\s*",
                RegexOptions.IgnoreCase
            )
            .Select(NormalizeNarrativeCarrier)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (carriers.Length == 0)
        {
            return [];
        }

        var amountSource = Regex.Replace(
            ratePart,
            @"/\s*(?:20|40|45)\s*['’]?\s*(?:GP|DV|DC|STD|ST|HC|HQ|NOR|RF)?\b",
            string.Empty,
            RegexOptions.IgnoreCase
        );
        var amounts = Regex.Matches(
                amountSource,
                @"(?:(?:USD|US\$|\$)\s*)?(?<amount>\d[\d,]*(?:\.\d+)?)",
                RegexOptions.IgnoreCase
            )
            .Select(match => match.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal))
            .ToArray();
        if (amounts.Length == 0)
        {
            return [];
        }

        if (amounts.Length == 1)
        {
            return carriers
                .Select(carrier => new NarrativeCarrierRate(carrier, amounts[0]))
                .ToArray();
        }

        return carriers
            .Select((carrier, index) => new NarrativeCarrierRate(
                carrier,
                amounts[Math.Min(index, amounts.Length - 1)]
            ))
            .ToArray();
    }

    private static string NormalizeNarrativeCarrier(string value)
    {
        var clean = Regex.Replace(value.Trim(), @"\b(?:NAC|FAK|BASKET)\b", string.Empty, RegexOptions.IgnoreCase)
            .Trim();
        return clean.ToUpperInvariant() switch
        {
            "MSK" or "MAERSK" => "MAERSK",
            "CMA" or "CMA CGM" => "CMA CGM",
            "HPL" => "HAPAG-LLOYD",
            _ => clean.ToUpperInvariant(),
        };
    }

    private static string? InferNarrativeContainerType(
        string rateLine,
        IReadOnlyList<string> source,
        int rateLineIndex
    )
    {
        static string? Parse(string value)
        {
            var match = Regex.Match(
                value,
                @"\b(?<size>20|40|45)\s*['’]?\s*(?<type>GP|DV|DC|STD|ST|HC|HQ|NOR|RF)?\b",
                RegexOptions.IgnoreCase
            );
            if (!match.Success)
            {
                return null;
            }

            var size = match.Groups["size"].Value;
            var type = match.Groups["type"].Value.ToUpperInvariant();
            if (size == "20")
            {
                return type is "HC" or "HQ" ? "20HC" : "20DV";
            }

            if (size == "45")
            {
                return "45HC";
            }

            return type is "HC" or "HQ" ? "40HC" : "40DV";
        }

        var direct = Parse(rateLine);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        // When the newest reply omits the equipment because it is inherited from
        // the contract thread, inspect only the nearest quoted rate sentence.
        foreach (var line in source.Skip(rateLineIndex + 1).Take(180))
        {
            if (!Regex.IsMatch(line, @"\b(?:rate|per)\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var inherited = Parse(line);
            if (!string.IsNullOrWhiteSpace(inherited))
            {
                return inherited;
            }
        }

        return null;
    }

    private static int FindNarrativeMessageEnd(IReadOnlyList<string> source, int start)
    {
        for (var index = start; index < source.Count; index++)
        {
            var line = source[index].Trim();
            if (
                line.StartsWith("Un saludo", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Regards", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Worldwide Logistics", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("发件人:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("De:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(line, @"^[_=-]{8,}$")
            )
            {
                return index;
            }
        }

        return source.Count;
    }

    private static IReadOnlyList<NarrativeRouteGroup> ParseNarrativeRouteGroups(
        IReadOnlyList<string> lines
    )
    {
        var result = new List<NarrativeRouteGroup>();
        string? origins = null;
        string? ports = null;
        string? commodity = null;
        string? rawOrigins = null;

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(origins) || string.IsNullOrWhiteSpace(ports))
            {
                origins = null;
                ports = null;
                commodity = null;
                rawOrigins = null;
                return;
            }

            var originVariants = SplitNarrativeRouteValue(origins)
                .Select(ParseNarrativeOriginVariant)
                .Where(value => !string.IsNullOrWhiteSpace(value.Port))
                .DistinctBy(
                    value => $"{value.Port}|{value.OriginCharge}",
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();
            var cleanedPorts = SplitNarrativeRouteValue(ports)
                .Select(RemoveNarrativeArbitraryCharge)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (originVariants.Length > 0 && cleanedPorts.Length > 0)
            {
                // Keep route lists compact for the mapping pipeline, but separate
                // origins that have different arbitrary charges. ColumnMappingService
                // expands each compact group into the final POL x POE combinations.
                foreach (var chargeGroup in originVariants.GroupBy(
                    value => value.OriginCharge,
                    StringComparer.OrdinalIgnoreCase
                ))
                {
                    result.Add(new NarrativeRouteGroup(
                        string.Join('/', chargeGroup.Select(value => value.Port)),
                        string.Join('/', cleanedPorts),
                        CleanValue(commodity ?? string.Empty),
                        rawOrigins,
                        chargeGroup.Key
                    ));
                }
            }

            origins = null;
            ports = null;
            commodity = null;
            rawOrigins = null;
        }

        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^[A-Z]\)$", RegexOptions.IgnoreCase))
            {
                Flush();
                continue;
            }

            var pair = Regex.Match(
                line,
                @"^(?<key>POL|POD|COMM(?:ODITY)?)\s*:\s*(?<value>.+)$",
                RegexOptions.IgnoreCase
            );
            if (!pair.Success)
            {
                continue;
            }

            var key = pair.Groups["key"].Value.ToUpperInvariant();
            var value = pair.Groups["value"].Value.Trim();
            switch (key)
            {
                case "POL":
                    if (!string.IsNullOrWhiteSpace(origins) && !string.IsNullOrWhiteSpace(ports))
                    {
                        Flush();
                    }
                    origins = value;
                    rawOrigins = value;
                    break;
                case "POD":
                    ports = value;
                    break;
                default:
                    commodity = value;
                    break;
            }
        }

        Flush();
        return result;
    }

    private static NarrativeRouteGroup MergeNarrativeGroups(
        IReadOnlyCollection<NarrativeRouteGroup> groups,
        IReadOnlyCollection<string> excludedOrigins
    )
    {
        var origins = groups
            .SelectMany(group => SplitNarrativeRouteValue(group.Origins))
            .Where(value => !excludedOrigins.Contains(
                NormalizeNarrativePortToken(value),
                StringComparer.OrdinalIgnoreCase
            ))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var ports = groups
            .SelectMany(group => SplitNarrativeRouteValue(group.PortsOfDischarge))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return new NarrativeRouteGroup(
            string.Join('/', origins),
            string.Join('/', ports),
            null,
            string.Join(" | ", groups.Select(group => group.RawOrigins).Where(value => !string.IsNullOrWhiteSpace(value))),
            null
        );
    }

    private static string BuildNarrativeRemarks(
        string carrier,
        NarrativeRouteGroup group,
        NarrativeRateOffer offer,
        string? surcharges,
        bool isOneNac
    )
    {
        var notes = new List<string> { "Producto comercial: NAC" };

        if (isOneNac && !string.IsNullOrWhiteSpace(group.Commodity))
        {
            notes.Add($"Mercancía autorizada para ONE NAC: {group.Commodity}");
        }

        if (!string.IsNullOrWhiteSpace(group.OriginCharge))
        {
            notes.Add($"Arbitrario de origen: USD {group.OriginCharge} por contenedor");
        }
        else if (!string.IsNullOrWhiteSpace(group.RawOrigins)
            && group.RawOrigins.Contains("arb", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"Arbitrarios por POL según fuente: {group.RawOrigins}");
        }

        if (carrier.Equals("MSC", StringComparison.OrdinalIgnoreCase)
            && offer.ExcludedOrigins.Count > 0)
        {
            notes.Add($"POL excluidos para la oferta general MSC: {string.Join('/', offer.ExcludedOrigins)}");
        }

        if (!string.IsNullOrWhiteSpace(surcharges))
        {
            notes.Add(surcharges.Trim().TrimEnd('.'));
        }

        if (carrier.Equals("MSC", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Las restricciones COMM detalladas en el correo aplican expresamente a ONE NAC");
        }

        return string.Join(". ", notes.Where(note => !string.IsNullOrWhiteSpace(note))) + ".";
    }

    private static IReadOnlyList<string> SplitNarrativeRouteValue(string value)
    {
        return value
            .Trim()
            // Preserve parentheses so the final POL keeps an arbitrary charge,
            // e.g. Chongqing(+arb USD850).
            .Trim('.', ';', ',')
            .Split(['/', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static NarrativeOriginVariant ParseNarrativeOriginVariant(string value)
    {
        var chargeMatch = Regex.Match(
            value,
            @"\(\s*\+?\s*arb(?:itrary)?\s+(?:USD|US\$|\$)\s*(?<amount>\d[\d,]*(?:\.\d+)?)\s*\)",
            RegexOptions.IgnoreCase
        );
        var charge = chargeMatch.Success
            ? chargeMatch.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal)
            : null;

        return new NarrativeOriginVariant(
            RemoveNarrativeArbitraryCharge(value),
            charge
        );
    }

    private static string? ParseNarrativePerContainerSurcharges(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        decimal total = 0m;
        var found = false;
        foreach (Match match in Regex.Matches(
            value,
            @"(?:USD|US\$|\$)\s*(?<amount>\d[\d,]*(?:\.\d+)?)\s*/\s*(?:cntr|container)\b",
            RegexOptions.IgnoreCase
        ))
        {
            var amountText = match.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (decimal.TryParse(
                amountText,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amount
            ))
            {
                total += amount;
                found = true;
            }
        }

        return found
            ? total.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static string RemoveNarrativeArbitraryCharge(string value)
    {
        return Regex.Replace(
            value,
            @"\s*\(\s*\+?\s*arb\s+USD\s*\d+(?:\.\d+)?\s*\)\s*",
            string.Empty,
            RegexOptions.IgnoreCase
        ).Trim();
    }

    private static string NormalizeNarrativePortToken(string value)
    {
        return ColumnHeaderNormalizer.Normalize(RemoveNarrativeArbitraryCharge(value));
    }

    private static string CleanNarrativeValidity(string value)
    {
        return value.Trim().Trim(',', '.', ';').Replace("/", " ", StringComparison.Ordinal);
    }

    private static (string? ValidFrom, string? ValidTo) SplitNarrativeValidity(string value)
    {
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        var sharedMonth = Regex.Match(
            normalized,
            @"^(?<from>\d{1,2})\s*[-–—]\s*(?<to>\d{1,2})\s+(?<month>[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{3,12})(?:\s+(?<year>\d{2,4}))?$",
            RegexOptions.IgnoreCase
        );
        if (sharedMonth.Success)
        {
            var suffix = sharedMonth.Groups["year"].Success
                ? $" {sharedMonth.Groups["year"].Value}"
                : string.Empty;
            return (
                $"{sharedMonth.Groups["from"].Value} {sharedMonth.Groups["month"].Value}{suffix}",
                $"{sharedMonth.Groups["to"].Value} {sharedMonth.Groups["month"].Value}{suffix}"
            );
        }

        return SplitValidityRange(normalized);
    }

    private sealed record NarrativeCarrierRate(string Carrier, string Amount);

    private sealed record NarrativeRateOffer(
        IReadOnlyList<NarrativeCarrierRate> CarrierRates,
        string Currency,
        string ContainerType,
        string ValidFrom,
        string ValidTo,
        string FreeDays,
        IReadOnlyList<string> ExcludedOrigins,
        string SourceLine
    );

    private sealed record NarrativeRouteGroup(
        string Origins,
        string PortsOfDischarge,
        string? Commodity,
        string? RawOrigins,
        string? OriginCharge
    );

    private sealed record NarrativeOriginVariant(
        string Port,
        string? OriginCharge
    );

    private static List<ExtractedTable> TryParseStackedFclTables(
        IReadOnlyCollection<string> lines
    )
    {
        var source = lines.ToArray();

        for (var headerStart = 0; headerStart < source.Length; headerStart++)
        {
            if (!string.Equals(
                    NormalizeStackedHeader(source[headerStart]),
                    "POL",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                continue;
            }

            var headers = new List<string>();
            var cursor = headerStart;

            while (cursor < source.Length && headers.Count < 14)
            {
                var header = NormalizeStackedHeader(source[cursor]);
                if (header is null)
                {
                    break;
                }

                headers.Add(header);
                cursor++;
            }

            if (!HasMinimumStackedFclHeaders(headers))
            {
                continue;
            }

            var rows = ParseStackedFclRows(source, cursor, headers);
            if (rows.Count == 0)
            {
                continue;
            }

            var sharedRemarks = FindStackedTableRemarks(source, cursor);
            if (!string.IsNullOrWhiteSpace(sharedRemarks))
            {
                rows = rows
                    .Select(row => AppendStackedTableRemarks(row, sharedRemarks))
                    .ToList();
            }

            var exposedHeaders = rows
                .SelectMany(row => row.Values.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return
            [
                new ExtractedTable(
                    "EMAIL FCL Cell Stream",
                    exposedHeaders,
                    rows
                ),
            ];
        }

        return [];
    }

    private static string? FindStackedTableRemarks(
        IReadOnlyList<string> source,
        int dataStart
    )
    {
        for (var index = dataStart; index < source.Count; index++)
        {
            var value = source[index].Trim();
            if (
                value.StartsWith("Sub to", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Subject to", StringComparison.OrdinalIgnoreCase)
            )
            {
                return CleanValue(value);
            }

            if (
                value.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("De:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Sent:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Enviado:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Asunto:", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(value, @"^[_=-]{8,}$")
            )
            {
                break;
            }
        }

        return null;
    }

    private static ExtractedRow AppendStackedTableRemarks(
        ExtractedRow row,
        string sharedRemarks
    )
    {
        var values = new Dictionary<string, string?>(
            row.Values,
            StringComparer.OrdinalIgnoreCase
        );
        values.TryGetValue("Remarks", out var currentRemarks);
        values["Remarks"] = string.IsNullOrWhiteSpace(currentRemarks)
            ? sharedRemarks
            : $"{currentRemarks}. {sharedRemarks}";

        return new ExtractedRow(
            row.RowNumber,
            values,
            JsonSerializer.Serialize(values)
        );
    }

    private static List<ExtractedRow> ParseStackedFclRows(
        IReadOnlyList<string> source,
        int dataStart,
        IReadOnlyList<string> headers
    )
    {
        var rows = new List<ExtractedRow>();
        var cursor = dataStart;
        var rowNumber = 2;

        while (cursor < source.Count)
        {
            if (IsStackedTableBoundary(source[cursor]))
            {
                break;
            }

            if (cursor + headers.Count > source.Count)
            {
                break;
            }

            var cells = source
                .Skip(cursor)
                .Take(headers.Count)
                .Select(CleanValue)
                .ToArray();

            if (!LooksLikeStackedFclRow(headers, cells))
            {
                // Once at least one valid row was found, a non-row marks the end of
                // the current table. This prevents importing older quoted tables.
                if (rows.Count > 0)
                {
                    break;
                }

                cursor++;
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                var header = headers[columnIndex];
                var value = cells[columnIndex];

                if (header.Equals("ValidityRange", StringComparison.OrdinalIgnoreCase))
                {
                    var (validFrom, validTo) = SplitValidityRange(value);
                    values["ValidFrom"] = validFrom;
                    values["ValidTo"] = validTo;
                    continue;
                }

                values[header] = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (
                values.TryGetValue("Carrier", out var rawCarrier)
                && TryExtractCarrierProduct(rawCarrier, out var carrierProduct)
            )
            {
                values["Remarks"] = $"Producto comercial: {carrierProduct}";
            }

            rows.Add(
                new ExtractedRow(
                    rowNumber,
                    values,
                    JsonSerializer.Serialize(values)
                )
            );
            rowNumber++;
            cursor += headers.Count;
        }

        return rows;
    }

    private static bool HasMinimumStackedFclHeaders(IReadOnlyCollection<string> headers)
    {
        return headers.Contains("POL", StringComparer.OrdinalIgnoreCase)
            && headers.Contains("POE", StringComparer.OrdinalIgnoreCase)
            && headers.Contains("Carrier", StringComparer.OrdinalIgnoreCase)
            && headers.Any(IsContainerAmountHeader);
    }

    private static bool LooksLikeStackedFclRow(
        IReadOnlyList<string> headers,
        IReadOnlyList<string> cells
    )
    {
        if (cells.Count < headers.Count)
        {
            return false;
        }

        string? Read(string header)
        {
            var index = headers
                .Select((value, position) => new { value, position })
                .FirstOrDefault(item => item.value.Equals(header, StringComparison.OrdinalIgnoreCase))
                ?.position;

            return index.HasValue && index.Value < cells.Count
                ? cells[index.Value]
                : null;
        }

        var origin = Read("POL");
        var destination = Read("POE");
        var carrier = Read("Carrier");
        var hasAmount = headers
            .Select((header, index) => new { header, index })
            .Where(item => IsContainerAmountHeader(item.header))
            .Any(item => item.index < cells.Count && LooksLikeMoney(cells[item.index]));

        return LooksLikeRouteCell(origin)
            && LooksLikeRouteCell(destination)
            && LooksLikeCarrierCell(carrier)
            && hasAmount;
    }

    private static string? NormalizeStackedHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim().Trim('|', ':').Trim();
        var normalized = ColumnHeaderNormalizer.Normalize(clean);

        if (normalized is "pol" or "portofloading" or "originport" or "origen")
        {
            return "POL";
        }

        if (normalized is "pod" or "portofdischarge" or "destinationport" or "destino")
        {
            // In carrier FCL matrices, POD means Port of Discharge. Dhole stores
            // that operational port as POE; a distinct Place of Delivery/final
            // destination is only populated when the source provides it.
            return "POE";
        }

        if (normalized is "carrier" or "naviera" or "shippingline")
        {
            return "Carrier";
        }

        if (normalized is "freetime" or "freedays" or "diaslibres")
        {
            return "Free Time";
        }

        if (
            normalized.StartsWith("validity", StringComparison.Ordinal)
            || normalized.StartsWith("vigencia", StringComparison.Ordinal)
            || normalized is "effective" or "effectivedate"
        )
        {
            return "ValidityRange";
        }

        if (IsContainerAmountHeader(clean))
        {
            return CanonicalContainerHeader(clean);
        }

        return null;
    }

    private static string CanonicalContainerHeader(string value)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(value);

        if (normalized.Contains("20", StringComparison.Ordinal))
        {
            return normalized.Contains("hc", StringComparison.Ordinal)
                || normalized.Contains("hq", StringComparison.Ordinal)
                    ? "20HC"
                    : "20GP";
        }

        if (normalized.Contains("45", StringComparison.Ordinal))
        {
            return "45HC";
        }

        if (normalized.Contains("nor", StringComparison.Ordinal))
        {
            return "40NOR";
        }

        return normalized.Contains("hc", StringComparison.Ordinal)
            || normalized.Contains("hq", StringComparison.Ordinal)
                ? "40HQ"
                : "40GP";
    }

    private static bool IsContainerAmountHeader(string? value)
    {
        return PricingContainerVariants.Expand(value).Count > 0
            || Regex.IsMatch(
                ColumnHeaderNormalizer.Normalize(value ?? string.Empty),
                @"^(20|40|45)(gp|dc|dv|hc|hq|nor|rf|reefer|ft|std|dry)?$",
                RegexOptions.IgnoreCase
            );
    }

    private static bool LooksLikeMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"(?:USD|EUR|CRC|\$|€|₡)?\s*\d{1,3}(?:[.,]\d{3})+(?:[.,]\d+)?|(?:USD|EUR|CRC|\$|€|₡)\s*\d+(?:[.,]\d+)?",
            RegexOptions.IgnoreCase
        );
    }

    private static bool LooksLikeRouteCell(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 250
            && !IsStackedTableBoundary(value)
            && !LooksLikeMoney(value);
    }

    private static bool LooksLikeCarrierCell(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 100
            && Regex.IsMatch(value, @"[A-Za-z]", RegexOptions.IgnoreCase)
            && !LooksLikeMoney(value);
    }

    private static bool TryExtractCarrierProduct(string? value, out string product)
    {
        product = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Regex.Match(
            value.Trim(),
            @"\b(?<product>FAK|BASKET|SPOT|PREMIUM)\s*$",
            RegexOptions.IgnoreCase
        );
        if (!match.Success)
        {
            return false;
        }

        product = match.Groups["product"].Value.ToUpperInvariant() switch
        {
            "BASKET" => "Basket",
            "SPOT" => "Spot",
            "PREMIUM" => "Premium",
            _ => "FAK",
        };
        return true;
    }

    private static (string? ValidFrom, string? ValidTo) SplitValidityRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var clean = Regex.Replace(value.Trim(), @"\s*\([^)]*\)\s*$", string.Empty).Trim();
        var fullDates = Regex.Match(
            clean,
            @"^(?<from>\d{1,2}\s*[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{3,12}(?:\s*\d{2,4})?)\s*[-–—]\s*(?<to>\d{1,2}\s*[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{3,12}(?:\s*\d{2,4})?)$",
            RegexOptions.IgnoreCase
        );

        if (fullDates.Success)
        {
            return (
                fullDates.Groups["from"].Value.Trim(),
                fullDates.Groups["to"].Value.Trim()
            );
        }

        var sharedMonth = Regex.Match(
            clean,
            @"^(?<fromDay>\d{1,2})\s*[-–—]\s*(?<toDay>\d{1,2})\s*(?<month>[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{3,12})(?:\s*(?<year>\d{2,4}))?$",
            RegexOptions.IgnoreCase
        );

        if (sharedMonth.Success)
        {
            var month = sharedMonth.Groups["month"].Value;
            var year = sharedMonth.Groups["year"].Success
                ? $" {sharedMonth.Groups["year"].Value}"
                : string.Empty;

            return (
                $"{sharedMonth.Groups["fromDay"].Value} {month}{year}",
                $"{sharedMonth.Groups["toDay"].Value} {month}{year}"
            );
        }

        return (null, clean);
    }

    private static bool IsStackedTableBoundary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var clean = value.Trim();
        var normalized = ColumnHeaderNormalizer.Normalize(clean);

        return clean.StartsWith("Sub to", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("P.S", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Un saludo", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Regards", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("De:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Sent:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Enviado:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Asunto:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("publishedfak", StringComparison.Ordinal)
            || normalized.StartsWith("website", StringComparison.Ordinal)
            || normalized.StartsWith("avisolegal", StringComparison.Ordinal)
            || normalized.StartsWith("theinformationcontained", StringComparison.Ordinal)
            || normalized.StartsWith("holidaynotice", StringComparison.Ordinal)
            || Regex.IsMatch(clean, @"^[_=-]{8,}$");
    }

    private static List<ExtractedTable> TryParseDelimitedTables(IReadOnlyCollection<string> lines)
    {
        var lineArray = lines.ToArray();
        var tables = new List<ExtractedTable>();

        for (var i = 0; i < lineArray.Length; i++)
        {
            var headerSplit = TrySplitHeaderLine(lineArray[i]);
            if (headerSplit is null)
            {
                continue;
            }

            var headerLayout = PrepareDelimitedHeaderLayout(headerSplit.Fields);
            var headers = NormalizeHeaders(headerLayout.Headers);
            var rows = new List<ExtractedRow>();

            for (var rowIndex = i + 1; rowIndex < lineArray.Length; rowIndex++)
            {
                var fields = SplitLine(lineArray[rowIndex], headerSplit.Mode);
                if (fields.Length < 2)
                {
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (ScoreHeaders(fields) >= 2)
                {
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (fields.Length < Math.Min(headers.Length, 3))
                {
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                {
                    values[headers[columnIndex]] = columnIndex < fields.Length && !string.IsNullOrWhiteSpace(fields[columnIndex])
                        ? fields[columnIndex].Trim()
                        : null;
                }

                if (headerLayout.IsCarrierFakMatrix)
                {
                    NormalizeCarrierFakMatrixValues(values);
                }

                if (values.Values.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    rows.Add(new ExtractedRow(rowIndex + 1, values, JsonSerializer.Serialize(values)));
                }
            }

            if (rows.Count > 0)
            {
                if (headerLayout.IsCarrierFakMatrix)
                {
                    var sharedRemarks = FindStackedTableRemarks(lineArray, i + 1);
                    if (!string.IsNullOrWhiteSpace(sharedRemarks))
                    {
                        rows = rows
                            .Select(row => AppendStackedTableRemarks(row, sharedRemarks))
                            .ToList();
                    }
                }

                var exposedHeaders = rows
                    .SelectMany(row => row.Values.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                tables.Add(
                    new ExtractedTable(
                        headerLayout.IsCarrierFakMatrix
                            ? "EMAIL FCL Matrix"
                            : "EMAIL",
                        exposedHeaders,
                        rows
                    )
                );
                break;
            }
        }

        return tables;
    }

    private static DelimitedHeaderLayout PrepareDelimitedHeaderLayout(
        IReadOnlyCollection<string> sourceHeaders
    )
    {
        var headers = sourceHeaders.Select(value => value.Trim()).ToList();
        var hadLeadingFakTitle = headers.Count > 1
            && ColumnHeaderNormalizer.Normalize(headers[0]) == "fak";

        if (hadLeadingFakTitle)
        {
            headers.RemoveAt(0);
        }

        var normalized = headers.Select(ColumnHeaderNormalizer.Normalize).ToArray();
        var isCarrierFakMatrix = (
                hadLeadingFakTitle
                || normalized.Any(value => value.StartsWith("validityetd", StringComparison.Ordinal))
            )
            && normalized.Contains("pol", StringComparer.OrdinalIgnoreCase)
            && normalized.Contains("pod", StringComparer.OrdinalIgnoreCase)
            && normalized.Any(value => value is "carrier" or "naviera" or "shippingline")
            && headers.Any(IsContainerAmountHeader);

        if (!isCarrierFakMatrix)
        {
            return new DelimitedHeaderLayout(headers.ToArray(), false);
        }

        var canonical = headers.Select(header =>
        {
            var token = ColumnHeaderNormalizer.Normalize(header);

            if (token == "pod")
            {
                return "POE";
            }

            if (
                token.StartsWith("validity", StringComparison.Ordinal)
                || token.StartsWith("vigencia", StringComparison.Ordinal)
            )
            {
                return "ValidityRange";
            }

            return IsContainerAmountHeader(header)
                ? CanonicalContainerHeader(header)
                : header;
        }).ToArray();

        return new DelimitedHeaderLayout(canonical, true);
    }

    private static void NormalizeCarrierFakMatrixValues(
        IDictionary<string, string?> values
    )
    {
        if (values.TryGetValue("ValidityRange", out var validity))
        {
            values.Remove("ValidityRange");
            var (validFrom, validTo) = SplitValidityRange(validity);
            values["ValidFrom"] = validFrom;
            values["ValidTo"] = validTo;
        }

        if (
            values.TryGetValue("Carrier", out var rawCarrier)
            && TryExtractCarrierProduct(rawCarrier, out var carrierProduct)
        )
        {
            values["Remarks"] = $"Producto comercial: {carrierProduct}";
        }
    }

    private static HeaderSplit? TrySplitHeaderLine(string line)
    {
        var candidates = new List<HeaderSplit>();

        AddDelimitedCandidate('|', LineSplitMode.Pipe, minimumOccurrences: 1);
        AddDelimitedCandidate('\t', LineSplitMode.Tab, minimumOccurrences: 1);
        AddDelimitedCandidate(';', LineSplitMode.Semicolon, minimumOccurrences: 1);
        AddDelimitedCandidate(',', LineSplitMode.Comma, minimumOccurrences: 2);

        var whitespaceFields = SplitLine(line, LineSplitMode.AlignedWhitespace);
        if (whitespaceFields.Length >= 2 && ScoreHeaders(whitespaceFields) >= 2)
        {
            candidates.Add(new HeaderSplit(whitespaceFields, LineSplitMode.AlignedWhitespace));
        }

        return candidates
            .OrderByDescending(candidate => ScoreHeaders(candidate.Fields))
            .ThenByDescending(candidate => candidate.Fields.Length)
            .FirstOrDefault();

        void AddDelimitedCandidate(char delimiter, LineSplitMode mode, int minimumOccurrences)
        {
            if (line.Count(character => character == delimiter) < minimumOccurrences)
            {
                return;
            }

            var fields = SplitLine(line, mode);
            if (fields.Length >= 2 && ScoreHeaders(fields) >= 2)
            {
                candidates.Add(new HeaderSplit(fields, mode));
            }
        }
    }

    private static IReadOnlyCollection<ExtractedTable> TryBuildMultiRowKeyValueTables(IReadOnlyCollection<string> lines)
    {
        var rows = new List<Dictionary<string, string?>>();
        var current = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var pairs = ExtractKeyValuePairs(line).ToArray();
            if (pairs.Length == 0)
            {
                continue;
            }

            foreach (var pair in pairs)
            {
                var canonicalKey = NormalizeEmailKey(pair.Key);

                if (canonicalKey is null || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                if (ShouldStartNewRow(current, canonicalKey))
                {
                    AddCurrentRow(rows, current);
                    current = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                }

                current[canonicalKey] = CleanValue(pair.Value);
            }
        }

        AddCurrentRow(rows, current);

        if (rows.Count == 0)
        {
            return [];
        }

        var headers = new[]
        {
            "Carrier",
            "Agent",
            "POL",
            "POE",
            "POD",
            "ContainerSize",
            "Commodity",
            "Currency",
            "FreightAmount",
            "FixedCosts",
            "ValidFrom",
            "ValidTo",
            "TransitTimeDays",
            "FreeDays",
            "Remarks",
        };

        var extractedRows = rows
            .Select((row, index) =>
            {
                var values = headers.ToDictionary(
                    header => header,
                    header => row.TryGetValue(header, out var value) && !string.IsNullOrWhiteSpace(value)
                        ? value
                        : null,
                    StringComparer.OrdinalIgnoreCase
                );

                return new ExtractedRow(index + 1, values, JsonSerializer.Serialize(values));
            })
            .ToArray();

        return [new ExtractedTable("EMAIL", headers, extractedRows)];
    }

    private static IEnumerable<(string Key, string Value)> ExtractKeyValuePairs(string line)
    {
        var normalizedLine = line.Trim().Trim('|', ';', ',').Trim();
        if (string.IsNullOrWhiteSpace(normalizedLine))
        {
            yield break;
        }

        var matches = Regex.Matches(
            normalizedLine,
            @"(?<key>[A-Za-zÁÉÍÓÚÜÑáéíóúüñ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9\s/_().-]{0,60})\s*[:=]\s*(?<value>.*?)(?=\s+[A-Za-zÁÉÍÓÚÜÑáéíóúüñ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9\s/_().-]{0,60}\s*[:=]|$)",
            RegexOptions.IgnoreCase
        );

        foreach (Match match in matches)
        {
            var key = match.Groups["key"].Value.Trim().Trim('|', ';', ',');
            var value = match.Groups["value"].Value.Trim().Trim('|', ';', ',');

            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                yield return (key, value);
            }
        }
    }

    private static bool ShouldStartNewRow(IReadOnlyDictionary<string, string?> current, string canonicalKey)
    {
        if (current.Count == 0)
        {
            return false;
        }

        if (!current.ContainsKey(canonicalKey))
        {
            return false;
        }

        return canonicalKey is "Carrier" or "POL" or "POE" or "POD" or "FreightAmount";
    }

    private static void AddCurrentRow(
        List<Dictionary<string, string?>> rows,
        Dictionary<string, string?> current
    )
    {
        if (current.Count == 0)
        {
            return;
        }

        var usefulValues = current.Values.Count(x => !string.IsNullOrWhiteSpace(x));
        var hasRoute = current.ContainsKey("POL")
            || current.ContainsKey("POE")
            || current.ContainsKey("POD");
        var hasAmount = current.ContainsKey("FreightAmount");
        var hasCarrier = current.ContainsKey("Carrier");

        if (usefulValues >= 4 && (hasRoute || hasAmount || hasCarrier))
        {
            rows.Add(new Dictionary<string, string?>(current, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static string? NormalizeEmailKey(string value)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(value);

        return normalized switch
        {
            "carrier" or "naviera" or "shippingline" or "lineamaritima" or "line" => "Carrier",
            "agent" or "agente" or "forwarder" or "provider" or "proveedor" => "Agent",
            "pol" or "origin" or "origen" or "originport" or "portofloading" or "loadingport" => "POL",
            "poe" or "portofexit" or "puertosalida" or "portofentry" or "entryport"
                or "puertoentrada" or "destination" or "destino" or "destinationport"
                or "puertodestino" or "portofdischarge" or "dischargeport" or "arrivalport"
                or "portofarrival" or "gateway" or "costaricagateway" or "transshipmentport"
                or "via" => "POE",
            "pod" or "placeofdelivery" or "delivery" or "deliveryplace"
                or "deliverypoint" or "finaldestination" or "finaldelivery"
                or "destinofinal" or "lugardeentrega" or "puntodeentrega" => "POD",
            "containersize" or "container" or "containertype" or "equipment" or "equipo" or "tipocontenedor" or "contenedor" => "ContainerSize",
            "commodity" or "mercancia" or "producto" or "cargo" => "Commodity",
            "currency" or "moneda" or "ccy" or "curr" => "Currency",
            "freightamount" or "freight" or "flete" or "oceanfreight" or "rate" or "tarifa" or "precio" or "amount" => "FreightAmount",
            "fixedcosts" or "fixedcost" or "costosfijos" or "costofijo" or "localcharges" or "charges" or "surcharges" => "FixedCosts",
            "validfrom" or "vigencia" or "vigenciadesde" or "inicio" or "fechainicio" or "start" or "startdate" or "desde" or "effectivefrom" or "effectivedate" => "ValidFrom",
            "validto" or "validuntil" or "vigenciahasta" or "vence" or "vencimiento" or "fechavencimiento" or "fin" or "fechafin" or "hasta" or "expiration" or "expirationdate" or "expiracion" or "validity" => "ValidTo",
            "transittimedays" or "transitdays" or "transittime" or "diastransito" or "tiempotransito" => "TransitTimeDays",
            "freedays" or "freetime" or "diaslibres" => "FreeDays",
            "remarks" or "observaciones" or "comentarios" or "comments" or "notes" => "Remarks",
            _ => null,
        };
    }

    private static ExtractedTable? TryBuildSingleKeyValueTable(string text)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Carrier"] = FindValue(text, "Carrier", "Naviera", "Shipping line", "Línea naviera"),
            ["Agent"] = FindValue(text, "Agent", "Agente", "Provider", "Proveedor"),
            ["POL"] = FindValue(text, "POL", "Origen", "Origin", "Port of Loading"),
            ["POE"] = FindValue(
                text,
                "POE",
                "Port of Exit",
                "Port of Entry",
                "Puerto salida",
                "Puerto entrada",
                "Destination Port",
                "Destino",
                "Destination",
                "Port of Discharge",
                "Arrival Port",
                "Gateway",
                "Via"
            ),
            ["POD"] = FindValue(
                text,
                "POD",
                "Place of Delivery",
                "Delivery Place",
                "Final Destination",
                "Destino final",
                "Lugar de entrega"
            ),
            ["ContainerSize"] = FindValue(text, "ContainerSize", "Container Size", "Container", "Equipment", "Equipo", "Tipo de contenedor"),
            ["Commodity"] = FindValue(text, "Commodity", "Mercancía", "Mercancia", "Producto"),
            ["Currency"] = FindValue(text, "Currency", "Moneda", "CCY") ?? InferCurrency(text),
            ["FreightAmount"] = FindValue(text, "FreightAmount", "Freight Amount", "Ocean Freight", "Freight", "Flete", "Tarifa", "Rate", "Precio"),
            ["FixedCosts"] = FindValue(text, "FixedCosts", "Fixed Costs", "Costos fijos", "Local Charges", "Surcharges", "Charges"),
            ["ValidFrom"] = FindValue(text, "Valid From", "Vigencia", "Vigencia desde", "Inicio", "Fecha inicio", "Desde", "Effective From"),
            ["ValidTo"] = FindValue(text, "Valid To", "Valid Until", "Vigencia hasta", "Vence", "Vencimiento", "Expiración", "Expiracion", "Hasta", "Expiration", "Validity"),
            ["TransitTimeDays"] = FindValue(text, "TransitTimeDays", "Transit Time Days", "Transit Days", "Días tránsito", "Dias transito"),
            ["FreeDays"] = FindValue(text, "Free Days", "Free time", "Días libres", "Dias libres"),
            ["Remarks"] = FindValue(text, "Remarks", "Observaciones", "Comentarios", "Notes"),
        };

        var usefulValues = values.Values.Count(x => !string.IsNullOrWhiteSpace(x));
        if (usefulValues < 4)
        {
            return null;
        }

        var headers = values.Keys.ToArray();
        var row = new ExtractedRow(1, values, JsonSerializer.Serialize(values));
        return new ExtractedTable("EMAIL", headers, [row]);
    }

    private static string? FindValue(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            var pattern = $@"(?:^|[\r\n\|;])\s*{Regex.Escape(label)}\s*[:=\-]?\s*(?<value>[^\r\n\|;]+)";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success)
            {
                var value = CleanValue(match.Groups["value"].Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? InferCurrency(string text)
    {
        var match = Regex.Match(text, @"\b(USD|EUR|CRC)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static string[] SplitLine(string line, LineSplitMode mode)
    {
        var cleanLine = line.Trim().Trim('|').Trim();

        return mode switch
        {
            LineSplitMode.Pipe => SplitByDelimiter(cleanLine, '|'),
            LineSplitMode.Tab => SplitByDelimiter(cleanLine, '\t'),
            LineSplitMode.Semicolon => SplitByDelimiter(cleanLine, ';'),
            LineSplitMode.Comma => SplitByDelimiter(cleanLine, ','),
            _ => Regex.Split(cleanLine, @"\s{2,}")
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray(),
        };
    }

    private static string[] SplitByDelimiter(string line, char delimiter)
    {
        return line
            .Split(delimiter, StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().Trim('|'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static int ScoreHeaders(IReadOnlyCollection<string> headers)
    {
        return headers.Count(header =>
        {
            var normalized = ColumnHeaderNormalizer.Normalize(header);
            return DefaultFclColumnMappings.Mappings.ContainsKey(normalized)
                || Regex.IsMatch(normalized, @"^(20|40|45)(gp|dc|dv|hc|hq|ft|std|dry)?(usd|rate|flete|tarifa|amount|precio|sale|venta)?$", RegexOptions.IgnoreCase);
        });
    }

    private static string[] NormalizeHeaders(IEnumerable<string> rawHeaders)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return rawHeaders.Select(header =>
        {
            var value = string.IsNullOrWhiteSpace(header) ? "Column" : header.Trim();
            if (!seen.TryAdd(value, 1))
            {
                seen[value]++;
                value = $"{value} {seen[value]}";
            }

            return value;
        }).ToArray();
    }

    private static string NormalizeLine(string value)
    {
        return value
            .Replace("¦", "|", StringComparison.Ordinal)
            .Replace("│", "|", StringComparison.Ordinal)
            .Replace("┃", "|", StringComparison.Ordinal)
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string CleanValue(string value)
    {
        return value
            .Trim()
            .Trim('|', ';', ',')
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Trim();
    }

    private enum LineSplitMode
    {
        Pipe,
        Tab,
        Semicolon,
        Comma,
        AlignedWhitespace,
    }

    private sealed record HeaderSplit(string[] Fields, LineSplitMode Mode);

    private sealed record DelimitedHeaderLayout(
        string[] Headers,
        bool IsCarrierFakMatrix
    );
}
