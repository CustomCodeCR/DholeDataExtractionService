namespace Dhole.DataExtraction.Contracts.Extraction;

public sealed record CatalogReferenceDto(
    Guid Id,
    string CatalogGroupSlug,
    string Code,
    string Slug,
    string Name,
    string? RawValue
);
