using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Files;

namespace Dhole.DataExtraction.Application.Extraction.DetectFileStructure;

public sealed class DetectFileStructureQueryHandler(
    IExtractionFileReader fileReader,
    IDocumentExtractorFactory extractorFactory
) : IQueryHandler<DetectFileStructureQuery, Result<DetectFileStructureResponse>>
{
    public async Task<Result<DetectFileStructureResponse>> HandleAsync(
        DetectFileStructureQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var file = await fileReader.ReadAsync(
            query.OriginalFileName,
            query.ContentType,
            query.FileContent,
            cancellationToken
        );

        if (!extractorFactory.CanExtract(file.SourceFileType))
        {
            return Result.Failure<DetectFileStructureResponse>(
                new CustomCodeFramework.Core.Results.Error(
                    "DataExtraction.UnsupportedFileType",
                    "El tipo de archivo no es soportado. Se permite PDF, Excel, CSV o correo/HTML."
                )
            );
        }

        var extractor = extractorFactory.GetExtractor(file.SourceFileType);

        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                file.OriginalFileName,
                file.ContentType,
                file.FileExtension,
                file.FileContent,
                query.ProfileCode
            ),
            cancellationToken
        );

        var tables = document
            .Tables.Select(table => new DetectedTableDto(
                table.SheetName,
                table.Headers,
                table.Rows.Count
            ))
            .ToArray();

        return Result.Success(
            new DetectFileStructureResponse(
                file.OriginalFileName,
                file.ContentType,
                file.FileExtension,
                file.FileSizeBytes,
                file.FileHash,
                file.SourceFileType.ToString(),
                tables,
                document.RawText
            )
        );
    }
}
