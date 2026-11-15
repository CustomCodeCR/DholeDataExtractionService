using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Infrastructure.Documentation;

// Extracted values must retain their source line. This is an evidence index,
// not a customs declaration or an AI confidence/accuracy guarantee.
public sealed record LogisticsEvidenceField(
    string Key, string Value, string Evidence, int Line, int? Page, bool FormatValidated
);
public sealed record LogisticsDocumentEvidence(
    string DocumentType, IReadOnlyList<LogisticsEvidenceField> Fields,
    IReadOnlyList<string> Warnings, bool RequiresHumanReview
);

public static class LogisticsDocumentEvidenceExtractor
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly (string Key, string Pattern)[] Labels =
    [
        ("documentNumber", @"^\s*(?:B\/L|Bill of Lading|House B\/L|Master B\/L|HBL|MBL|AWB|Air Waybill|Carta de Porte|CMR|Manifest|Manifiesto)\s*(?:No\.?|Number|N[°ºo]\.?|#)?\s*[:#\-]\s*(?<value>[A-Z0-9][A-Z0-9\/\-]{3,38})\s*$"),
        ("invoiceNumber", @"^\s*(?:Commercial Invoice|Invoice|Factura Comercial|Factura)\s*(?:No\.?|Number|N[°ºo]\.?|#)?\s*[:#\-]\s*(?<value>[A-Z0-9][A-Z0-9\/\-]{2,38})\s*$"),
        ("shipper", @"^\s*(?:Shipper|Exporter|Exportador|Remitente)\s*[:\-]\s*(?<value>.+?)\s*$"),
        ("consignee", @"^\s*(?:Consignee|Consignatario|Importador|Importer)\s*[:\-]\s*(?<value>.+?)\s*$"),
        ("notifyParty", @"^\s*(?:Notify Party|Notificar a)\s*[:\-]\s*(?<value>.+?)\s*$"),
        ("vessel", @"^\s*(?:Vessel|Buque|Vessel Name)\s*[:\-]\s*(?<value>.+?)\s*$"),
        ("voyage", @"^\s*(?:Voyage|Viaje|Voyage No\.?)\s*[:\-]\s*(?<value>[A-Z0-9\/\-]{2,30})\s*$"),
        ("flight", @"^\s*(?:Flight|Vuelo|Flight No\.?)\s*[:\-]\s*(?<value>[A-Z0-9\/\-]{2,30})\s*$"),
        ("pol", @"^\s*(?:POL|Port of Loading|Puerto de Carga|Aeropuerto de Origen)\s*[:\-]\s*(?<value>.+?)\s*$"),
        ("pod", @"^\s*(?:POD|Port of Discharge|Puerto de Descarga|Aeropuerto de Destino)\s*[:\-]\s*(?<value>.+?)\s*$"),
        ("placeOfDelivery", @"^\s*(?:Place of Delivery|Lugar de Entrega)\s*[:\-]\s*(?<value>.+?)\s*$"),
        ("grossWeight", @"^\s*(?:Gross Weight|Peso Bruto)\s*[:\-]\s*(?<value>\d[\d., ]*\s*(?:KG|KGS|LB|LBS|TON|MT))\s*$"),
        ("netWeight", @"^\s*(?:Net Weight|Peso Neto)\s*[:\-]\s*(?<value>\d[\d., ]*\s*(?:KG|KGS|LB|LBS|TON|MT))\s*$"),
        ("packages", @"^\s*(?:No\.? of Packages|Packages|Bultos|Cantidad de Bultos)\s*[:\-]\s*(?<value>\d[\d., ]*(?:\s+[A-Za-z]+)?)\s*$"),
        ("totalAmount", @"^\s*(?:Invoice Total|Total Invoice|Total Amount|Valor Total|Monto Total|Total Factura)\s*[:\-]\s*(?<value>(?:(?:USD|EUR|CRC|US\$|\$)\s*)?\d[\d., ]*(?:\s*(?:USD|EUR|CRC))?)\s*$"),
        ("sealNumber", @"^\s*(?:Seal(?: Number| No\.?)?|Precinto|Marchamo)\s*[:#\-]\s*(?<value>[A-Z0-9\-]{4,30})\s*$"),
    ];

    public static LogisticsDocumentEvidence Analyze(string source, string fileName)
    {
        ArgumentNullException.ThrowIfNull(source);
        var probe = (Path.GetFileNameWithoutExtension(fileName) + "\n" +
            source[..Math.Min(source.Length, 6000)]);
        var type = IdentifyType(probe);
        var fields = new List<LogisticsEvidenceField>();
        var warnings = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = source.Split('\n');
        int? currentPage = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim().TrimEnd('\r');
            if (line.Length == 0 || line.Length > 1500)
                continue;
            var page = Regex.Match(line, @"^## P[áa]gina\s+(\d+)", Options, MatchTimeout);
            if (page.Success && int.TryParse(page.Groups[1].Value, out var number))
                currentPage = number;

            foreach (var (key, pattern) in Labels)
            {
                var match = Regex.Match(line, pattern, Options, MatchTimeout);
                if (!match.Success)
                    continue;
                var value = match.Groups["value"].Value.Trim();
                if (value.Length == 0 || value.Length > 250)
                    continue;
                if (seen.TryGetValue(key, out var previous))
                {
                    if (!previous.Equals(value, StringComparison.OrdinalIgnoreCase) &&
                        !warnings.Contains($"Conflicto en {key}; revisar documento original."))
                        warnings.Add($"Conflicto en {key}; revisar documento original.");
                    continue;
                }
                seen[key] = value;
                fields.Add(new LogisticsEvidenceField(key, value, line, index + 1,
                    currentPage, false));
            }

            foreach (Match match in Regex.Matches(
                line, @"(?<![A-Z0-9])[A-Z]{4}\d{7}(?![A-Z0-9])", Options, MatchTimeout))
            {
                var code = match.Value.ToUpperInvariant();
                if (seen.ContainsKey("container:" + code))
                    continue;
                seen["container:" + code] = code;
                var valid = IsValidIso6346(code);
                fields.Add(new LogisticsEvidenceField("containerNumber", code,
                    line, index + 1, currentPage, valid));
                if (!valid)
                    warnings.Add($"Contenedor {code}: dígito verificador ISO 6346 no válido; revisar OCR.");
            }
        }

        if (type == "Unknown")
            warnings.Add("Tipo documental no confirmado; requiere clasificación manual.");
        if (fields.Count == 0)
            warnings.Add("No se localizaron campos con evidencia textual suficiente.");
        if (source.Contains("[CONTENIDO INTERMEDIO OMITIDO", StringComparison.OrdinalIgnoreCase)
            || source.Contains("[CONTENIDO DE PÁGINA RECORTADO", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Contenido truncado: algunos campos pueden faltar.");
        warnings.Add("Extracción preliminar: cotejar campos con el original antes de trámites, declaraciones o importación.");
        return new LogisticsDocumentEvidence(type, fields, warnings, true);
    }

    public static string IdentifyType(string probe)
    {
        if (Regex.IsMatch(probe, @"\b(?:HOUSE\s+(?:B\/L|BILL OF LADING)|HBL)\b", Options, MatchTimeout)) return "HBL";
        if (Regex.IsMatch(probe, @"\b(?:MASTER\s+(?:B\/L|BILL OF LADING)|MBL)\b", Options, MatchTimeout)) return "MBL";
        if (Regex.IsMatch(probe, @"\b(?:AIR\s*WAYBILL|AWB)\b", Options, MatchTimeout)) return "AWB";
        if (Regex.IsMatch(probe, @"\b(?:CARTA\s+DE\s+PORTE|CONSIGNMENT\s+NOTE|CMR)\b", Options, MatchTimeout)) return "CartaPorte";
        if (Regex.IsMatch(probe, @"\b(?:PACKING\s+LIST|LISTA\s+DE\s+EMPAQUE)\b", Options, MatchTimeout)) return "PackingList";
        if (Regex.IsMatch(probe, @"\b(?:COMMERCIAL\s+INVOICE|FACTURA\s+COMERCIAL)\b", Options, MatchTimeout)) return "CommercialInvoice";
        if (Regex.IsMatch(probe, @"\b(?:CARGO\s+MANIFEST|MANIFIESTO\s+DE\s+CARGA)\b", Options, MatchTimeout)) return "CargoManifest";
        if (Regex.IsMatch(probe, @"\b(?:BILL\s+OF\s+LADING|B\/L|SEA\s+WAYBILL)\b", Options, MatchTimeout)) return "BillOfLading";
        if (Regex.IsMatch(probe, @"\b(?:TARIFARIO|TARIFF|FREIGHT\s+RATES?|TARIFAS?)\b", Options, MatchTimeout)) return "Tariff";
        return "Unknown";
    }

    // ISO 6346: owner/category letters mapped to 10..38, skipping multiples of 11.
    public static bool IsValidIso6346(string code)
    {
        if (!Regex.IsMatch(code, @"^[A-Z]{4}\d{7}$", RegexOptions.CultureInvariant))
            return false;
        long total = 0;
        for (var index = 0; index < 10; index++)
        {
            var c = code[index];
            var number = char.IsDigit(c) ? c - '0' :
                (c - 'A' + 10) + (c - 'A' + 10) / 11;
            total += (long)number * (1 << index);
        }
        return (total % 11) % 10 == code[10] - '0';
    }
}
