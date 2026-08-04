namespace Dhole.DataExtraction.Application.Abstractions.Services;

public interface IImportProfileResolver
{
    Task<ResolvedImportProfile> ResolveAsync(
        string? requestedProfileCode,
        CancellationToken cancellationToken = default
    );
}

public sealed record ResolvedImportProfile(
    ConfigCatalogItemResult Item,
    string MappingProfileCode,
    string RawValue
);
