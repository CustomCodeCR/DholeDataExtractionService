using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

/// <summary>
/// Recognizes FCL quotations that price several containers in a single document.
/// The resulting monetary values are explicitly per container and EXCLUDE VAT.
/// The original line items, quantity and invoice reconciliation remain in RawJson.
/// A tax/total mismatch requires manual review before Average may use the rate.
/// </summary>
public static class MultiContainerFclQuotationParser
{
    private static readonly Regex Freight = new(
        @"^\s*FLETE\s+(?:MAR[IÍ]TIMO|INTERNACIONAL)\s+\$?\s*(?<price>\d[\d.,]*)\s+(?<equipment>(?:20|40|45)\s*(?:HQ|HC|GP|DV|DC|STD|ST|DRY))\s+(?<qty>[1-9]\d{0,2})\s+(?:CNTR|CONTENEDORES?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Charge = new(
        @"^\s*(?<name>MANEJOS(?:\s+EN\s+CR)?|INTERNO|CARGOS?\s+DE\s+NAVIERA|INGRESO\s+A\s+PREDIO(?:\s*-\s*TRANSITORIO)?|REDESTINO|TRANSPORTE\s+INTERNO)\s+\$?\s*(?<price>\d[\d.,]*)\s+(?<qty>[1-9]\d{0,2})\s+(?<basis>CNTR|BL|HBL|CONTENEDOR(?:ES)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static List<ExtractedTable> TryParse(IReadOnlyCollection<string> lines)
    {
        // Do not hijack ordinary FCL carrier rate matrices or single-container quotes.
        var source = string.Join("\n", lines);
        if (!Regex.IsMatch(source, @"\bTARIFA\s+FCL\b", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(source, @"\bCONCEPTO\b", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(source, @"\bCALCULO\b", RegexOptions.IgnoreCase))
            return [];

        var lineArray = lines.ToArray();
        var freightMatch = lineArray.Select(line => Freight.Match(line)).FirstOrDefault(m => m.Success);
        if (freightMatch is null
            || !TryMoney(freightMatch.Groups["price"].Value, out var oceanUnit)
            || !int.TryParse(freightMatch.Groups["qty"].Value, out var quantity)
            || quantity <= 1)
            return [];

        var equipment = Regex.Replace(freightMatch.Groups["equipment"].Value, @"\s+", "")
            .ToUpperInvariant().Replace("HQ", "HC", StringComparison.Ordinal);
        if (equipment.EndsWith("ST", StringComparison.Ordinal))
            equipment = equipment[..^2] + "DV";

        var charges = new List<QuotationCharge>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tableStarted = false;
        foreach (var line in lineArray)
        {
            if (!tableStarted)
            {
                if (!Freight.IsMatch(line))
                    continue;
                tableStarted = true;
                continue;
            }

            if (Regex.IsMatch(line, @"^\s*(?:MONTO\s+GRAVADO|TOTAL\s+DOLARES|ADICIONALES)\b",
                    RegexOptions.IgnoreCase))
                break;

            var match = Charge.Match(line);
            if (!match.Success
                || !TryMoney(match.Groups["price"].Value, out var price)
                || !int.TryParse(match.Groups["qty"].Value, out var chargeQuantity))
                continue;

            var basis = match.Groups["basis"].Value.ToUpperInvariant();
            basis = basis is "BL" or "HBL" ? "PerBillOfLading" : "PerContainer";
            var name = Regex.Replace(match.Groups["name"].Value.Trim(), @"\s+", " ");
            var key = $"{name}|{price}|{chargeQuantity}|{basis}";
            if (!seen.Add(key))
                continue;

            // A charge billed per BL cannot be multiplied by the number of containers.
            if (basis == "PerContainer" && chargeQuantity != quantity)
                return []; // ambiguous equipment/charge allocation -> AI/manual review

            charges.Add(new QuotationCharge(name, price, chargeQuantity, basis));
        }

        if (charges.Count == 0)
            return [];

        var quoteDate = FindDate(source);
        var expiryDate = FindExpiryDate(source);
        if (!quoteDate.HasValue || !expiryDate.HasValue || expiryDate < quoteDate)
            return [];

        var oceanTotal = oceanUnit * quantity;
        var localTotal = charges.Sum(c => c.UnitPrice * c.Quantity);
        var preTaxTotal = oceanTotal + localTotal;
        var tax = FindAmount(lineArray, @"^\s*IVA\s+\$?\s*(?<amount>\d[\d.,]*)");
        var invoiceTotal = FindAmount(lineArray,
            @"^\s*TOTAL\s+DOLARES\s+IVA\s+INCLUIDO\s+\$?\s*(?<amount>\d[\d.,]*)");

        // An invoice total is VAT-inclusive; Average must never treat it as the FCL unit rate.
        var mismatch = !tax.HasValue || !invoiceTotal.HasValue
            || Math.Abs(preTaxTotal + tax.Value - invoiceTotal.Value) > 0.02m;

        var destinationUnit = charges
            .Where(c => !IsInland(c.Name))
            .Sum(c => c.UnitPrice * c.Quantity) / quantity;
        var inlandUnit = charges
            .Where(c => IsInland(c.Name))
            .Sum(c => c.UnitPrice * c.Quantity) / quantity;
        var saleUnit = preTaxTotal / quantity;

        var pol = FindLabel(source, @"\b(?:ORIGEN|POL)\s*:\s*(?<value>[A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑ \-]+?)(?:\s{2,}|$)");
        var poe = FindLabel(source, @"\bPOD\s*:\s*(?<value>[A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑ \-]+?)(?:\s{2,}|$)");
        var destination = FindLabel(source, @"\bDESTINO\s*:\s*(?<value>[A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑ \-]+?)(?:\s{2,}|\s+POL\s*:|$)");
        var carrier = FindLabel(source, @"\bCARRIER\s*:\s*(?<value>[A-Z0-9][A-Z0-9 \-]*?)(?:\s{2,}|$)");
        if (string.IsNullOrWhiteSpace(pol) || string.IsNullOrWhiteSpace(poe)
            || string.IsNullOrWhiteSpace(carrier))
            return [];

        var freeDaysMatch = Regex.Match(source, @"\b(?<days>\d{1,2})\s*(?:FREETIME|FREE\s*TIME)\b",
            RegexOptions.IgnoreCase);
        var transitMatch = Regex.Match(source,
            @"TIEMPO\s+DE\s+TRANSITO\s*\*?\s*:\s*(?:\d{1,3}\s*-\s*)?(?<days>\d{1,3})",
            RegexOptions.IgnoreCase);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OriginPort"] = pol,
            ["PortOfExit"] = poe,
            ["DestinationPort"] = destination,
            ["ContainerType"] = equipment,
            ["Carrier"] = carrier,
            ["Currency"] = "USD",
            ["ValidFrom"] = quoteDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["ValidTo"] = expiryDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["OceanFreight"] = Money(oceanUnit),
            ["DestinationCharges"] = Money(destinationUnit),
            ["Surcharges"] = Money(inlandUnit),
            ["TotalSale"] = Money(saleUnit),
            ["FreeDays"] = freeDaysMatch.Success ? freeDaysMatch.Groups["days"].Value : null,
            ["TransitDays"] = transitMatch.Success ? transitMatch.Groups["days"].Value : null,
            ["Quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
            ["RateBasis"] = "PerContainer",
            ["MultiContainerQuotation"] = "true",
            ["UnitPricesNormalized"] = "true",
            ["RequiresManualReview"] = mismatch ? "true" : "false",
            ["QuotedInvoiceTotal"] = invoiceTotal.HasValue ? Money(invoiceTotal.Value) : null,
            ["QuotedVat"] = tax.HasValue ? Money(tax.Value) : null,
            ["ReconstructedPreTaxTotal"] = Money(preTaxTotal),
            ["InvoiceDiscrepancy"] = tax.HasValue && invoiceTotal.HasValue
                ? Money(preTaxTotal + tax.Value - invoiceTotal.Value) : null,
            ["LineItemsJson"] = JsonSerializer.Serialize(
                new[] { new QuotationCharge("FLETE MARITIMO", oceanUnit, quantity, "PerContainer") }
                    .Concat(charges)),
            ["Remarks"] = mismatch
                ? "Cotización multcontenedor: el total del documento no concilia con los rubros e IVA; revisar antes de Average."
                : "Cotización multcontenedor: importes unitarios sin IVA; detalle original conservado."
        };
        return [new ExtractedTable(
            "FCL multi-container quotation",
            values.Keys.ToArray(),
            [new ExtractedRow(1, values, JsonSerializer.Serialize(values))]
        )];
    }

    private static bool IsInland(string label) =>
        Regex.IsMatch(label, @"\b(?:INTERNO|TRANSPORTE\s+INTERNO)\b", RegexOptions.IgnoreCase);

    private static string Money(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool TryMoney(string raw, out decimal value)
    {
        // US format in the recognized quote: 7,100.00, 750.00, 100.00.
        return decimal.TryParse(raw, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
            CultureInfo.GetCultureInfo("en-US"), out value) && value >= 0m;
    }

    private static decimal? FindAmount(IEnumerable<string> lines, string pattern)
    {
        foreach (var line in lines)
        {
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (match.Success && TryMoney(match.Groups["amount"].Value, out var value))
                return value;
        }
        return null;
    }

    private static string? FindLabel(string source, string pattern)
    {
        var match = Regex.Match(source, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static DateTime? FindDate(string source)
    {
        var match = Regex.Match(source, @"\b(?<date>\d{1,2}/\d{1,2}/20\d{2})\b");
        return match.Success && DateTime.TryParseExact(match.Groups["date"].Value,
            "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value : null;
    }

    private static DateTime? FindExpiryDate(string source)
    {
        var match = Regex.Match(source,
            @"VALIDEZ\s+AL\s+(?<day>\d{1,2})\s+DE\s+(?<month>[A-ZÁÉÍÓÚ]+)\s+(?<year>20\d{2})",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ENERO"] = 1, ["FEBRERO"] = 2, ["MARZO"] = 3, ["ABRIL"] = 4,
            ["MAYO"] = 5, ["JUNIO"] = 6, ["JULIO"] = 7, ["AGOSTO"] = 8,
            ["SEPTIEMBRE"] = 9, ["SETIEMBRE"] = 9, ["OCTUBRE"] = 10,
            ["NOVIEMBRE"] = 11, ["DICIEMBRE"] = 12
        };
        return months.TryGetValue(match.Groups["month"].Value, out var month)
            ? new DateTime(int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
                month, int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture))
            : null;
    }

    private sealed record QuotationCharge(string Name, decimal UnitPrice, int Quantity, string Basis);
}
