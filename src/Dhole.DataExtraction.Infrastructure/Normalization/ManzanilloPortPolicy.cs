using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Services;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

/// <summary>
/// Disambiguates the two commercial ports called Manzanillo. An unqualified
/// Manzanillo is treated as Panama, never Mexico. Explicit Mexican geography
/// is authoritative and must never be silently reassigned to Panama.
/// </summary>
internal static class ManzanilloPortPolicy
{
    internal enum Country { NotManzanillo, Panama, Mexico, Conflicting }

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly Regex Manzanillo = new(
        @"\bMANZANILLO\b", Options | RegexOptions.Compiled, Timeout
    );
    private static readonly Regex Mexico = new(
        @"\b(?:MEXICO|MÉXICO|MX|COLIMA|MXZLO)\b",
        Options | RegexOptions.Compiled, Timeout
    );
    private static readonly Regex Panama = new(
        @"\b(?:PANAMA|PANAMÁ|PA|COLON|COLÓN|PAONX|MIT)\b",
        Options | RegexOptions.Compiled, Timeout
    );
    private static readonly Regex StandalonePort = new(
        @"^\s*(?:(?:PUERTO|PTO)\s+(?:DE\s+)?)?MANZANILLO\b",
        Options | RegexOptions.Compiled, Timeout
    );

    internal static Country RequestedCountry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Manzanillo.IsMatch(value))
            return Country.NotManzanillo;

        var mexico = Mexico.IsMatch(value);
        var panama = Panama.IsMatch(value);
        if (mexico && panama)
            return Country.Conflicting;
        return mexico ? Country.Mexico : Country.Panama;
    }

    internal static string? NormalizeStandalonePort(string value)
    {
        // Composite origins (e.g. "Manzanillo/Veracruz") must not be collapsed
        // into one port. The catalog resolver handles standalone names only.
        if (!StandalonePort.IsMatch(value) || value.Contains('/') || value.Contains(';')
            || value.Contains('|'))
            return null;

        return RequestedCountry(value) switch
        {
            Country.Panama => "MANZANILLO, PANAMA",
            Country.Mexico => "MANZANILLO, MEXICO",
            _ => null,
        };
    }

    internal static bool MatchesCatalogItem(
        ConfigCatalogItemResult item,
        Country expected,
        IReadOnlyCollection<string> catalogAliases
    )
    {
        if (expected is not (Country.Panama or Country.Mexico))
            return false;

        var identity = string.Join(" ", new[]
        {
            item.Name, item.Slug, item.Code, item.Value ?? string.Empty
        });
        if (!Manzanillo.IsMatch(identity) && !catalogAliases.Any(Manzanillo.IsMatch))
            return false;

        // Identity beats an unqualified alias. Mexican items often advertise
        // "Manzanillo" as an alias, which must not override their country.
        var identityCountry = RequestedCountry(identity);
        if (identityCountry == Country.Conflicting)
            return false;

        if (identityCountry == Country.NotManzanillo)
        {
            var explicitCountries = catalogAliases
                .Where(alias => Manzanillo.IsMatch(alias))
                .Select(RequestedCountry)
                .Where(country => country == Country.Mexico)
                .Distinct()
                .ToArray();
            if (explicitCountries.Length > 0)
                identityCountry = Country.Mexico;
        }

        // No country in the configured identity means the business default:
        // Manzanillo (Panama). Do not pick a Mexico candidate on a fuzzy match.
        return identityCountry == expected;
    }
}
