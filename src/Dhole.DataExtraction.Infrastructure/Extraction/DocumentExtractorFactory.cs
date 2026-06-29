using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Infrastructure.Extraction;

public sealed class DocumentExtractorFactory(IEnumerable<IDocumentExtractor> extractors)
    : IDocumentExtractorFactory
{
    private readonly IReadOnlyDictionary<SourceFileType, IDocumentExtractor> _extractors =
        extractors.ToDictionary(x => x.FileType);

    public IDocumentExtractor GetExtractor(SourceFileType fileType)
    {
        if (_extractors.TryGetValue(fileType, out var extractor))
        {
            return extractor;
        }

        throw new NotSupportedException($"No existe extractor para el tipo de archivo {fileType}.");
    }

    public bool CanExtract(SourceFileType fileType)
    {
        return _extractors.ContainsKey(fileType);
    }
}
