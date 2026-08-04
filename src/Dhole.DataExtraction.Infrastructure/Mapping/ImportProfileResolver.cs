using System.Globalization;
using System.Text;
using System.Text.Json;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;

namespace Dhole.DataExtraction.Infrastructure.Mapping;

public sealed class ImportProfileResolver(IConfigCatalogClient configCatalogClient)
    : IImportProfileResolver
{
    public async Task<ResolvedImportProfile> ResolveAsync(
        string? requestedProfileCode,
        CancellationToken cancellationToken = default
    )
    {
        ConfigCatalogItemResult profile;
        string rawValue;

        if (!string.IsNullOrWhiteSpace(requestedProfileCode))
        {
            rawValue = requestedProfileCode.Trim();
            profile = await configCatalogClient.ResolveCatalogItemAsync(
                    PricingCatalogSlugs.ImportProfiles,
                    rawValue,
                    cancellationToken
                )
                ?? throw new InvalidOperationException(
                    $"El perfil '{rawValue}' no existe o está inactivo en el catálogo '{PricingCatalogSlugs.ImportProfiles}'."
                );
        }
        else
        {
            var activeProfiles = (await configCatalogClient.GetActiveCatalogItemsByGroupAsync(
                    PricingCatalogSlugs.ImportProfiles,
                    cancellationToken
                ))
                .Where(item => item.IsActive)
                .DistinctBy(item => item.Id)
                .ToArray();

            profile = SelectAutomaticProfile(activeProfiles);
            rawValue = FirstNotEmpty(profile.Value, profile.Code, profile.Slug, profile.Name)!;
        }

        var mappingProfileCode = FirstNotEmpty(profile.Value, profile.Code, profile.Slug);
        if (string.IsNullOrWhiteSpace(mappingProfileCode))
        {
            throw new InvalidOperationException(
                $"El perfil '{profile.Name}' no define un código de mapeo utilizable. Configure Value, Code o Slug en '{PricingCatalogSlugs.ImportProfiles}'."
            );
        }

        return new ResolvedImportProfile(profile, mappingProfileCode, rawValue);
    }

    private static ConfigCatalogItemResult SelectAutomaticProfile(
        IReadOnlyCollection<ConfigCatalogItemResult> activeProfiles
    )
    {
        if (activeProfiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"No existe ningún perfil activo en el catálogo '{PricingCatalogSlugs.ImportProfiles}'."
            );
        }

        if (activeProfiles.Count == 1)
        {
            return activeProfiles.Single();
        }

        var standardProfiles = activeProfiles
            .Where(IsStandardProfile)
            .ToArray();

        if (standardProfiles.Length == 1)
        {
            return standardProfiles[0];
        }

        if (standardProfiles.Length > 1)
        {
            throw new InvalidOperationException(
                $"Hay más de un perfil marcado como estándar en '{PricingCatalogSlugs.ImportProfiles}'. Deje únicamente uno activo o predeterminado."
            );
        }

        throw new InvalidOperationException(
            $"Hay {activeProfiles.Count} perfiles activos en '{PricingCatalogSlugs.ImportProfiles}' y ninguno puede identificarse como estándar. Deje uno solo activo o marque uno como predeterminado."
        );
    }

    private static bool IsStandardProfile(ConfigCatalogItemResult item)
    {
        string?[] values = [item.Code, item.Slug, item.Name, item.Value];
        if (values.Any(IsStandardValue))
        {
            return true;
        }

        return MetadataMarksAsStandard(item.MetadataJson);
    }

    private static bool IsStandardValue(string? value)
    {
        var normalized = NormalizeKey(value);
        if (normalized.Length == 0)
        {
            return false;
        }

        string[] markers = ["standard", "estandar", "default", "predeterminado"];
        return markers.Any(marker =>
            normalized.Equals(marker, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(marker + "-", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("-" + marker, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("-" + marker + "-", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static bool MetadataMarksAsStandard(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var propertyName = NormalizeKey(property.Name).Replace("-", string.Empty);
                if (propertyName is not (
                    "default"
                    or "isdefault"
                    or "standard"
                    or "isstandard"
                    or "predeterminado"
                    or "espredeterminado"
                    or "estandar"
                    or "esestandar"
                ))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out var numericValue)
                    && numericValue == 1)
                {
                    return true;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = NormalizeKey(property.Value.GetString());
                    if (value is "true" or "1" or "yes" or "si" or "standard" or "estandar")
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var appendSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (appendSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                appendSeparator = false;
            }
            else
            {
                appendSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string? FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
