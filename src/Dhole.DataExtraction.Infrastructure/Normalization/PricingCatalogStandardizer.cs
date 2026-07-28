using System.Globalization;
using System.Text;
using System.Text.Json;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.ValueObjects;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

public sealed class PricingCatalogStandardizer(IConfigCatalogClient configCatalogClient)
    : IPricingCatalogStandardizer
{
    public async Task StandardizeAsync(
        IReadOnlyCollection<PricingExtractionRecord> records,
        Guid? updatedBy = null,
        CancellationToken cancellationToken = default
    )
    {
        if (records.Count == 0)
        {
            return;
        }

        var catalogTasks = PricingCatalogSlugs.RowCatalogs.ToDictionary(
            slug => slug,
            slug => configCatalogClient.GetActiveCatalogItemsByGroupAsync(
                slug,
                cancellationToken
            ),
            StringComparer.OrdinalIgnoreCase
        );

        await Task.WhenAll(catalogTasks.Values);

        var catalogItems = catalogTasks.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Result.Where(item => item.IsActive).ToArray(),
            StringComparer.OrdinalIgnoreCase
        );

        var matchers = catalogItems.ToDictionary(
            pair => pair.Key,
            pair => new CatalogMatcher(pair.Key, pair.Value),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            record.ApplyCatalogReferences(
                ToReference(matchers[PricingCatalogSlugs.Pol].Resolve(record.OriginPort), record.OriginPort),
                ToReference(matchers[PricingCatalogSlugs.Poe].Resolve(record.PortOfExit), record.PortOfExit),
                ToReference(matchers[PricingCatalogSlugs.Pod].Resolve(record.DestinationPort), record.DestinationPort),
                ToReference(matchers[PricingCatalogSlugs.ContainerTypes].Resolve(record.ContainerType), record.ContainerType),
                ToReference(matchers[PricingCatalogSlugs.Carriers].Resolve(record.Carrier), record.Carrier),
                ToReference(matchers[PricingCatalogSlugs.Agents].Resolve(record.Agent), record.Agent),
                ToReference(matchers[PricingCatalogSlugs.Currencies].Resolve(record.Currency), record.Currency),
                updatedBy
            );
        }
    }

    private static CatalogItemReference? ToReference(
        ConfigCatalogItemResult? item,
        string? rawValue
    )
    {
        return item is null
            ? null
            : CatalogItemReference.Create(
                item.Id,
                item.CatalogGroupSlug,
                item.Code,
                item.Slug,
                item.Name,
                rawValue
            );
    }

    private sealed class CatalogMatcher
    {
        private static readonly HashSet<string> GenericPortTokens = new(
            [
                "PORT",
                "PUERTO",
                "PTO",
                "TERMINAL",
                "PORTOF",
                "DE",
                "DEL",
                "LA",
                "EL",
                "LOS",
                "LAS",
                "OF",
                "THE",
                "AT",
                "MARITIME",
                "MARITIMO",
                "MARITIMA",
            ],
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly HashSet<string> GenericCarrierTokens = new(
            [
                "LINE", "LINES", "SHIPPING", "COMPANY", "CO", "LTD", "LIMITED",
                "CORPORATION", "CORP", "GROUP", "SA", "SAS", "INC"
            ],
            StringComparer.OrdinalIgnoreCase
        );

        private readonly string _groupSlug;
        private readonly CatalogCandidate[] _candidates;

        public CatalogMatcher(
            string groupSlug,
            IReadOnlyCollection<ConfigCatalogItemResult> items
        )
        {
            _groupSlug = groupSlug;
            _candidates = items.Select(item => new CatalogCandidate(groupSlug, item)).ToArray();
        }

        public ConfigCatalogItemResult? Resolve(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            // Agents are commercial counterparties. A fuzzy or partial match can
            // assign a rate to the wrong company, so only a complete code/slug/
            // name/value or an explicit Config alias is accepted.
            if (_groupSlug == PricingCatalogSlugs.Agents)
            {
                var strictKey = NormalizeLookupKey(rawValue);
                var strictMatches = _candidates
                    .Where(candidate => candidate.StrictKeys.Contains(strictKey))
                    .Select(candidate => candidate.Item)
                    .DistinctBy(item => item.Id)
                    .ToArray();

                return strictMatches.Length == 1 ? strictMatches[0] : null;
            }

            var inputKeys = BuildComparisonKeys(_groupSlug, rawValue).ToHashSet(
                StringComparer.OrdinalIgnoreCase
            );

            var exactMatches = _candidates
                .Where(candidate => candidate.Keys.Overlaps(inputKeys))
                .Select(candidate => candidate.Item)
                .DistinctBy(item => item.Id)
                .ToArray();

            if (exactMatches.Length == 1)
            {
                return exactMatches[0];
            }

            if (exactMatches.Length > 1)
            {
                return null;
            }

            // Coincidencia direccional solicitada para valores abreviados provenientes
            // de documentos y correos: el valor canónico de Config contiene el texto
            // recibido. Ejemplos: "Puerto de Moín" contiene "MOIN" y
            // "Colón/Manzanillo" contiene "COLON" o "MANZANILLO".
            var containsMatches = _candidates
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Distance = GetDirectionalContainmentDistance(inputKeys, candidate.Keys),
                })
                .Where(result => result.Distance.HasValue)
                .OrderBy(result => result.Distance!.Value)
                .ThenBy(result => result.Candidate.Item.Name)
                .ToArray();

            if (containsMatches.Length == 1)
            {
                return containsMatches[0].Candidate.Item;
            }

            if (
                containsMatches.Length > 1
                && containsMatches[0].Distance!.Value < containsMatches[1].Distance!.Value
            )
            {
                return containsMatches[0].Candidate.Item;
            }

            if (containsMatches.Length > 1)
            {
                // Si dos elementos de Config contienen el mismo texto con igual
                // especificidad, no se asigna un ID potencialmente incorrecto.
                return null;
            }

            var inputTokenSets = BuildTokenSets(_groupSlug, rawValue).ToArray();
            var scored = _candidates
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Score = CalculateScore(inputKeys, inputTokenSets, candidate),
                })
                .Where(result => result.Score >= MinimumScore(_groupSlug))
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Candidate.Item.Name)
                .ToArray();

            if (scored.Length == 0)
            {
                return null;
            }

            if (scored.Length > 1 && scored[0].Score - scored[1].Score < 0.04m)
            {
                // Una coincidencia ambigua nunca debe convertirse en un ID de Config incorrecto.
                return null;
            }

            return scored[0].Candidate.Item;
        }

        private static int? GetDirectionalContainmentDistance(
            IReadOnlyCollection<string> inputKeys,
            IReadOnlyCollection<string> catalogKeys
        )
        {
            int? bestDistance = null;

            foreach (var inputKey in inputKeys.Where(IsSafeContainmentKey))
            {
                foreach (var catalogKey in catalogKeys.Where(IsSafeContainmentKey))
                {
                    if (
                        catalogKey.Length < inputKey.Length
                        || !catalogKey.Contains(inputKey, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    var distance = catalogKey.Length - inputKey.Length;
                    bestDistance = !bestDistance.HasValue || distance < bestDistance.Value
                        ? distance
                        : bestDistance;
                }
            }

            return bestDistance;
        }

        private static bool IsSafeContainmentKey(string key)
        {
            return key.Length >= 4
                && key.Any(char.IsLetter)
                && !Guid.TryParseExact(key, "N", out _);
        }

        private static decimal CalculateScore(
            IReadOnlyCollection<string> inputKeys,
            IReadOnlyCollection<HashSet<string>> inputTokenSets,
            CatalogCandidate candidate
        )
        {
            var best = 0m;

            foreach (var inputKey in inputKeys.Where(key => key.Length >= 3))
            {
                foreach (var candidateKey in candidate.Keys.Where(key => key.Length >= 3))
                {
                    if (inputKey.Contains(candidateKey, StringComparison.OrdinalIgnoreCase)
                        || candidateKey.Contains(inputKey, StringComparison.OrdinalIgnoreCase))
                    {
                        var shortest = Math.Min(inputKey.Length, candidateKey.Length);
                        var longest = Math.Max(inputKey.Length, candidateKey.Length);
                        var ratio = longest == 0 ? 0m : (decimal)shortest / longest;

                        // Las abreviaturas cortas solo son aceptables si cubren completamente un token.
                        if (shortest >= 4 || ratio >= 0.75m)
                        {
                            best = Math.Max(best, 0.86m + (ratio * 0.1m));
                        }
                    }

                    if (inputKey.Length >= 4 && candidateKey.Length >= 4)
                    {
                        var similarity = CalculateSimilarity(inputKey, candidateKey);
                        best = Math.Max(best, similarity);
                    }
                }
            }

            foreach (var inputTokens in inputTokenSets)
            {
                foreach (var candidateTokens in candidate.TokenSets)
                {
                    if (inputTokens.Count == 0 || candidateTokens.Count == 0)
                    {
                        continue;
                    }

                    var intersection = inputTokens.Intersect(candidateTokens).Count();
                    if (intersection == 0)
                    {
                        continue;
                    }

                    var candidateCoverage = (decimal)intersection / candidateTokens.Count;
                    var inputCoverage = (decimal)intersection / inputTokens.Count;
                    var score = (candidateCoverage * 0.65m) + (inputCoverage * 0.35m);

                    if (candidateCoverage == 1m)
                    {
                        score = Math.Max(score, 0.92m);
                    }

                    best = Math.Max(best, score);

                    foreach (var inputToken in inputTokens.Where(token => token.Length >= 3))
                    {
                        foreach (
                            var candidateToken in candidateTokens.Where(token => token.Length >= 3)
                        )
                        {
                            if (
                                inputToken.Equals(
                                    candidateToken,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                best = Math.Max(best, 0.98m);
                                continue;
                            }

                            var shortestLength = Math.Min(
                                inputToken.Length,
                                candidateToken.Length
                            );
                            if (shortestLength < 4)
                            {
                                continue;
                            }

                            var tokenSimilarity = CalculateSimilarity(
                                inputToken,
                                candidateToken
                            );
                            best = Math.Max(best, tokenSimilarity * 0.98m);

                            if (
                                inputToken.StartsWith(
                                    candidateToken,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || candidateToken.StartsWith(
                                    inputToken,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                var longestLength = Math.Max(
                                    inputToken.Length,
                                    candidateToken.Length
                                );
                                best = Math.Max(
                                    best,
                                    0.84m
                                        + (
                                            0.12m
                                            * decimal.Divide(
                                                shortestLength,
                                                longestLength
                                            )
                                        )
                                );
                            }
                        }
                    }
                }
            }

            return Math.Min(best, 1m);
        }

        private static decimal MinimumScore(string groupSlug)
        {
            return groupSlug switch
            {
                PricingCatalogSlugs.Currencies => 0.94m,
                PricingCatalogSlugs.ContainerTypes => 0.88m,
                PricingCatalogSlugs.Agents => 0.84m,
                PricingCatalogSlugs.Carriers => 0.82m,
                PricingCatalogSlugs.Pol
                    or PricingCatalogSlugs.Poe
                    or PricingCatalogSlugs.Pod => 0.78m,
                _ => 0.84m,
            };
        }

        private static decimal CalculateSimilarity(string left, string right)
        {
            var distance = LevenshteinDistance(left, right);
            var maxLength = Math.Max(left.Length, right.Length);
            return maxLength == 0 ? 1m : 1m - ((decimal)distance / maxLength);
        }

        private static int LevenshteinDistance(string left, string right)
        {
            var previous = new int[right.Length + 1];
            var current = new int[right.Length + 1];

            for (var index = 0; index <= right.Length; index++)
            {
                previous[index] = index;
            }

            for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
            {
                current[0] = leftIndex;
                for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
                {
                    var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                    current[rightIndex] = Math.Min(
                        Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                        previous[rightIndex - 1] + substitutionCost
                    );
                }

                (previous, current) = (current, previous);
            }

            return previous[right.Length];
        }

        private sealed class CatalogCandidate
        {
            public CatalogCandidate(string groupSlug, ConfigCatalogItemResult item)
            {
                Item = item;
                var values = GetCatalogValues(item).ToArray();
                StrictKeys = values
                    .Select(NormalizeLookupKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                Keys = values
                    .SelectMany(value => BuildComparisonKeys(groupSlug, value))
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                TokenSets = values
                    .SelectMany(value => BuildTokenSets(groupSlug, value))
                    .Where(tokens => tokens.Count > 0)
                    .ToArray();
            }

            public ConfigCatalogItemResult Item { get; }
            public HashSet<string> StrictKeys { get; }
            public HashSet<string> Keys { get; }
            public IReadOnlyCollection<HashSet<string>> TokenSets { get; }
        }

        private static IEnumerable<string> GetCatalogValues(ConfigCatalogItemResult item)
        {
            yield return item.Id.ToString();
            yield return item.Code;
            yield return item.Slug;
            yield return item.Name;

            if (!string.IsNullOrWhiteSpace(item.Value))
            {
                yield return item.Value;
            }

            foreach (var alias in ReadAliases(item.MetadataJson))
            {
                yield return alias;
            }
        }

        private static IEnumerable<string> BuildComparisonKeys(string groupSlug, string value)
        {
            var baseKey = NormalizeLookupKey(value);
            if (!string.IsNullOrWhiteSpace(baseKey))
            {
                yield return baseKey;
            }

            var specialized = groupSlug switch
            {
                PricingCatalogSlugs.Pol or PricingCatalogSlugs.Poe or PricingCatalogSlugs.Pod =>
                    PortNameNormalizer.Normalize(value),
                PricingCatalogSlugs.ContainerTypes => ContainerTypeNormalizer.Normalize(value),
                PricingCatalogSlugs.Carriers => CarrierNameNormalizer.Normalize(value),
                PricingCatalogSlugs.Currencies => NormalizeCurrency(value),
                _ => null,
            };

            var specializedKey = NormalizeLookupKey(specialized);
            if (!string.IsNullOrWhiteSpace(specializedKey))
            {
                yield return specializedKey;
            }

            foreach (var token in Tokenize(value, groupSlug).Where(token => token.Length >= 3))
            {
                yield return token;
            }

            if (!string.IsNullOrWhiteSpace(specialized))
            {
                foreach (
                    var token in Tokenize(specialized, groupSlug)
                        .Where(token => token.Length >= 3)
                )
                {
                    yield return token;
                }
            }

            if (groupSlug == PricingCatalogSlugs.Carriers)
            {
                var acronym = BuildAcronym(value);
                if (!string.IsNullOrWhiteSpace(acronym))
                {
                    yield return acronym;
                }
            }
        }

        private static IEnumerable<HashSet<string>> BuildTokenSets(
            string groupSlug,
            string value
        )
        {
            var tokens = Tokenize(value, groupSlug);
            if (tokens.Count > 0)
            {
                yield return tokens;
            }

            var specialized = groupSlug switch
            {
                PricingCatalogSlugs.Pol or PricingCatalogSlugs.Poe or PricingCatalogSlugs.Pod =>
                    PortNameNormalizer.Normalize(value),
                PricingCatalogSlugs.ContainerTypes => ContainerTypeNormalizer.Normalize(value),
                PricingCatalogSlugs.Carriers => CarrierNameNormalizer.Normalize(value),
                PricingCatalogSlugs.Currencies => NormalizeCurrency(value),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(specialized))
            {
                var specializedTokens = Tokenize(specialized, groupSlug);
                if (specializedTokens.Count > 0 && !specializedTokens.SetEquals(tokens))
                {
                    yield return specializedTokens;
                }
            }
        }

        private static HashSet<string> Tokenize(string value, string groupSlug)
        {
            var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(char.IsLetterOrDigit(character)
                    ? char.ToUpperInvariant(character)
                    : ' ');
            }

            var tokens = builder
                .ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Length >= 2)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (groupSlug is PricingCatalogSlugs.Pol or PricingCatalogSlugs.Poe or PricingCatalogSlugs.Pod)
            {
                tokens.ExceptWith(GenericPortTokens);
            }
            else if (groupSlug == PricingCatalogSlugs.Carriers)
            {
                tokens.ExceptWith(GenericCarrierTokens);
            }

            return tokens;
        }

        private static string? NormalizeCurrency(string value)
        {
            var key = NormalizeLookupKey(value);
            return key switch
            {
                "USD" or "US" or "USDOLLAR" or "USDOLLARS" or "DOLLAR"
                    or "DOLLARS" or "DOLAR" or "DOLARES" or "US$" or "$" => "USD",
                "CRC" or "COLON" or "COLONES" or "COSTARICANCOLON"
                    or "COSTARICANCOLONES" => "CRC",
                "EUR" or "EURO" or "EUROS" => "EUR",
                "CNY" or "RMB" or "YUAN" or "RENMINBI" => "CNY",
                _ => value.Trim().ToUpperInvariant(),
            };
        }

        private static string? BuildAcronym(string value)
        {
            var words = value
                .Split(
                    [' ', '-', '/', '.', ',', '(', ')'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Where(word => word.Any(char.IsLetterOrDigit))
                .ToArray();

            if (words.Length < 2)
            {
                return null;
            }

            var acronym = new string(
                words
                    .Select(word => word.FirstOrDefault(char.IsLetterOrDigit))
                    .Where(character => character != default)
                    .Select(char.ToUpperInvariant)
                    .ToArray()
            );

            return acronym.Length is >= 2 and <= 8 ? acronym : null;
        }

        private static IReadOnlyCollection<string> ReadAliases(string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                return [];
            }

            try
            {
                using var document = JsonDocument.Parse(metadataJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return [];
                }

                var aliases = new List<string>();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!IsAliasProperty(property.Name))
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        aliases.AddRange(
                            property.Value
                                .EnumerateArray()
                                .Where(element => element.ValueKind == JsonValueKind.String)
                                .Select(element => element.GetString())
                                .Where(value => !string.IsNullOrWhiteSpace(value))
                                .Select(value => value!)
                        );
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        aliases.AddRange(
                            (property.Value.GetString() ?? string.Empty)
                                .Split(
                                    [',', ';', '|'],
                                    StringSplitOptions.RemoveEmptyEntries
                                        | StringSplitOptions.TrimEntries
                                )
                        );
                    }
                }

                return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static bool IsAliasProperty(string name)
        {
            return name.Equals("aliases", StringComparison.OrdinalIgnoreCase)
                || name.Equals("alias", StringComparison.OrdinalIgnoreCase)
                || name.Equals("synonyms", StringComparison.OrdinalIgnoreCase)
                || name.Equals("alternativeNames", StringComparison.OrdinalIgnoreCase)
                || name.Equals("abbreviations", StringComparison.OrdinalIgnoreCase)
                || name.Equals("keywords", StringComparison.OrdinalIgnoreCase)
                || name.Equals("searchTerms", StringComparison.OrdinalIgnoreCase)
                || name.Equals("codes", StringComparison.OrdinalIgnoreCase)
                || name.Equals("unlocodes", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character)
                    == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToUpperInvariant(character));
                }
            }

            return builder.ToString();
        }
    }
}
