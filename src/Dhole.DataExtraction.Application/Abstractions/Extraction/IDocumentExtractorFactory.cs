using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

public interface IDocumentExtractorFactory
{
    IDocumentExtractor GetExtractor(SourceFileType fileType);

    bool CanExtract(SourceFileType fileType);
}
