namespace Dhole.DataExtraction.Domain.Extraction.ValueObjects;

public sealed class CatalogItemReference
{
    private CatalogItemReference() { }

    private CatalogItemReference(
        Guid catalogItemId,
        string catalogGroupSlug,
        string code,
        string slug,
        string name,
        string? rawValue
    )
    {
        CatalogItemId = catalogItemId;
        CatalogGroupSlug = catalogGroupSlug.Trim().ToLowerInvariant();
        Code = code.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Name = name.Trim();
        RawValue = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim();
    }

    public Guid CatalogItemId { get; private set; }
    public string CatalogGroupSlug { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? RawValue { get; private set; }

    public static CatalogItemReference Create(
        Guid catalogItemId,
        string catalogGroupSlug,
        string code,
        string slug,
        string name,
        string? rawValue
    )
    {
        if (catalogItemId == Guid.Empty)
        {
            throw new InvalidOperationException("El identificador del catálogo es requerido.");
        }

        if (string.IsNullOrWhiteSpace(catalogGroupSlug)
            || string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(slug)
            || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "La referencia de catálogo debe contener grupo, código, slug y nombre."
            );
        }

        return new CatalogItemReference(
            catalogItemId,
            catalogGroupSlug,
            code,
            slug,
            name,
            rawValue
        );
    }
}
