using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Infrastructure.Files;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.Email;

public sealed partial class EmailAgentResolver(
    IConfigCatalogClient configCatalogClient,
    ILogger<EmailAgentResolver> logger
) : IEmailAgentResolver
{
    private const decimal MinimumFuzzyScore = 0.88m;
    private const decimal MinimumWinningMargin = 0.06m;

    private static readonly HashSet<string> LegalCompanyTokens = new(
        [
            "SA", "SAS", "SRL", "LTDA", "LIMITADA", "LLC", "LTD", "LIMITED",
            "INC", "CORP", "CORPORATION", "CO", "COMPANY", "SOCIEDAD",
            "ANONIMA", "ANONIMO"
        ],
        StringComparer.OrdinalIgnoreCase
    );

    public async Task ApplyFromEmailAsync(
        IReadOnlyCollection<PricingExtractionRecord> records,
        string? subject,
        string? bodyText,
        string? bodyHtml,
        Guid? updatedBy = null,
        CancellationToken cancellationToken = default
    )
    {
        if (records.Count == 0
            || (string.IsNullOrWhiteSpace(subject)
                && string.IsNullOrWhiteSpace(bodyText)
                && string.IsNullOrWhiteSpace(bodyHtml)))
        {
            return;
        }

        var agents = (await configCatalogClient.GetActiveCatalogItemsByGroupAsync(
                PricingCatalogSlugs.Agents,
                cancellationToken
            ))
            .Where(item => item.IsActive)
            .Select(BuildCandidate)
            .ToArray();
        if (agents.Length == 0)
        {
            return;
        }

        var subjectText = EmailSubjectNormalizer.NormalizeForExtraction(subject);
        var plainBody = BuildPlainBody(bodyText, bodyHtml);
        var contextualAgent = ResolveFromText(subjectText, agents, isSubject: true)
            ?? ResolveFromText(plainBody, agents, isSubject: false);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Para correos, el agente identificado en el asunto/cuerpo representa
            // al proveedor del tarifario y tiene prioridad sobre valores inferidos
            // dentro de las filas por el extractor o por AI.
            if (contextualAgent is not null)
            {
                record.AssignAgentFromEmail(contextualAgent.Item.Name, updatedBy);
                continue;
            }

            var explicitAgent = ResolveExplicitValue(record.Agent, agents);
            if (explicitAgent is not null
                && !string.Equals(
                    record.Agent,
                    explicitAgent.Item.Name,
                    StringComparison.Ordinal
                ))
            {
                record.AssignAgentFromEmail(explicitAgent.Item.Name, updatedBy);
            }
        }

        if (contextualAgent is not null)
        {
            logger.LogInformation(
                "Agente {AgentName} resuelto desde {AgentSource} del correo con puntuación {Score:0.00}.",
                contextualAgent.Item.Name,
                contextualAgent.Source,
                contextualAgent.Score
            );
        }
    }

    private static AgentCandidate? ResolveExplicitValue(
        string? value,
        IReadOnlyCollection<AgentCandidate> candidates
    )
    {
        var normalized = NormalizeCompanyText(value);
        if (normalized.Length == 0)
        {
            return null;
        }

        var matches = candidates
            .Where(candidate => candidate.Aliases.Any(alias =>
                alias.Normalized.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            ))
            .ToArray();
        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (matches.Length > 1)
        {
            return null;
        }

        return ResolveFromText(value, candidates, isSubject: true)?.Candidate;
    }

    private static AgentMatch? ResolveFromText(
        string? source,
        IReadOnlyCollection<AgentCandidate> candidates,
        bool isSubject
    )
    {
        var normalizedSource = NormalizeCompanyText(source);
        if (normalizedSource.Length == 0)
        {
            return null;
        }

        var sourceTokens = Tokenize(normalizedSource);
        var matches = candidates
            .Select(candidate => new AgentMatch(
                candidate,
                ScoreCandidate(candidate, normalizedSource, sourceTokens, isSubject),
                isSubject ? "asunto" : "cuerpo"
            ))
            .Where(match => match.Score >= MinimumFuzzyScore)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Candidate.LongestAliasLength)
            .ThenBy(match => match.Candidate.Item.Name)
            .ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length == 1)
        {
            return matches[0];
        }

        return matches[0].Score - matches[1].Score >= MinimumWinningMargin
            ? matches[0]
            : null;
    }

    private static decimal ScoreCandidate(
        AgentCandidate candidate,
        string normalizedSource,
        IReadOnlyList<string> sourceTokens,
        bool allowShortAliases
    )
    {
        var best = 0m;
        foreach (var alias in candidate.Aliases)
        {
            if (alias.Normalized.Length == 0)
            {
                continue;
            }

            if (alias.Tokens.Length == 1 && alias.Normalized.Length <= 3)
            {
                if (allowShortAliases && ContainsWholePhrase(normalizedSource, alias.Normalized))
                {
                    best = Math.Max(best, 1m);
                }

                continue;
            }

            if (ContainsWholePhrase(normalizedSource, alias.Normalized))
            {
                best = Math.Max(best, 1m);
                continue;
            }

            var windowScore = BestWindowSimilarity(sourceTokens, alias.Tokens);
            best = Math.Max(best, windowScore);
        }

        return best;
    }

    private static bool ContainsWholePhrase(string source, string phrase)
    {
        if (source.Equals(phrase, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return source.StartsWith(phrase + " ", StringComparison.OrdinalIgnoreCase)
            || source.EndsWith(" " + phrase, StringComparison.OrdinalIgnoreCase)
            || source.Contains(" " + phrase + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal BestWindowSimilarity(
        IReadOnlyList<string> sourceTokens,
        IReadOnlyList<string> aliasTokens
    )
    {
        if (sourceTokens.Count == 0 || aliasTokens.Count == 0)
        {
            return 0m;
        }

        var minimumWindow = Math.Max(1, aliasTokens.Count - 1);
        var maximumWindow = Math.Min(sourceTokens.Count, aliasTokens.Count + 1);
        var alias = string.Concat(aliasTokens);
        var best = 0m;

        for (var windowLength = minimumWindow; windowLength <= maximumWindow; windowLength++)
        {
            for (var index = 0; index + windowLength <= sourceTokens.Count; index++)
            {
                var window = string.Concat(sourceTokens.Skip(index).Take(windowLength));
                var maximumLength = Math.Max(alias.Length, window.Length);
                if (maximumLength == 0)
                {
                    continue;
                }

                var distance = LevenshteinDistance(alias, window);
                var score = 1m - (decimal)distance / maximumLength;
                best = Math.Max(best, score);
            }
        }

        return best;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

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

    private static AgentCandidate BuildCandidate(ConfigCatalogItemResult item)
    {
        var rawAliases = new List<string?>
        {
            item.Name,
            item.Code,
            item.Slug,
            item.Value,
        };
        rawAliases.AddRange(ReadMetadataAliases(item.MetadataJson));

        var aliases = rawAliases
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeCompanyText(value))
            .Where(value => value.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new AgentAlias(value, Tokenize(value).ToArray()))
            .ToArray();

        return new AgentCandidate(
            item,
            aliases,
            aliases.Length == 0 ? 0 : aliases.Max(alias => alias.Normalized.Length)
        );
    }

    private static IReadOnlyCollection<string> ReadMetadataAliases(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return ReadAliases(document.RootElement).ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ReadAliases(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!AliasPropertyRegex().IsMatch(property.Name))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    foreach (var alias in value.Split(
                        [',', ';', '|'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    ))
                    {
                        yield return alias;
                    }
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        yield return item.GetString()!;
                    }
                }
            }
        }
    }

    private static string BuildPlainBody(string? bodyText, string? bodyHtml)
    {
        var text = string.IsNullOrWhiteSpace(bodyText)
            ? StripHtml(bodyHtml)
            : bodyText;
        var cleaned = TextContentDecoder.Clean(WebUtility.HtmlDecode(text ?? string.Empty));
        return cleaned.Length <= 20_000 ? cleaned : cleaned[..20_000];
    }

    private static string StripHtml(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : HtmlTagRegex().Replace(value, " ");
    }

    private static string NormalizeCompanyText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in TextContentDecoder.Clean(WebUtility.HtmlDecode(value))
            .Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToUpperInvariant(character)
                : ' ');
        }

        var tokens = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        RemoveTrailingLegalInitials(tokens);

        return string.Join(
            ' ',
            tokens.Where(token => !LegalCompanyTokens.Contains(token)).ToArray()
        );
    }

    private static void RemoveTrailingLegalInitials(List<string> tokens)
    {
        string[][] suffixes =
        [
            ["S", "A", "S"],
            ["S", "R", "L"],
            ["L", "T", "D", "A"],
            ["S", "A"],
        ];

        foreach (var suffix in suffixes)
        {
            if (tokens.Count < suffix.Length)
            {
                continue;
            }

            var start = tokens.Count - suffix.Length;
            if (suffix.Select((token, index) =>
                    token.Equals(tokens[start + index], StringComparison.OrdinalIgnoreCase)
                ).All(matches => matches))
            {
                tokens.RemoveRange(start, suffix.Length);
                return;
            }
        }
    }

    private static IReadOnlyList<string> Tokenize(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex("alias|aliases|alternat|synonym|search", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AliasPropertyRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    private sealed record AgentAlias(string Normalized, string[] Tokens);

    private sealed record AgentCandidate(
        ConfigCatalogItemResult Item,
        AgentAlias[] Aliases,
        int LongestAliasLength
    );

    private sealed record AgentMatch(
        AgentCandidate Candidate,
        decimal Score,
        string Source
    )
    {
        public ConfigCatalogItemResult Item => Candidate.Item;
    }
}
