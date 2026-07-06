using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Files;

namespace Dhole.DataExtraction.Application.Extraction.PreviewColumnMapping;

public sealed class PreviewColumnMappingQueryHandler(
    IExtractionFileReader fileReader,
    IDocumentExtractorFactory extractorFactory,
    IColumnMappingService columnMapping
) : IQueryHandler<PreviewColumnMappingQuery, Result<PreviewColumnMappingResponse>>
{
    public async Task<Result<PreviewColumnMappingResponse>> HandleAsync(
        PreviewColumnMappingQuery query,
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
            return Result.Failure<PreviewColumnMappingResponse>(
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

        var preview = await columnMapping.PreviewAsync(
            document,
            query.ProfileCode,
            cancellationToken
        );

        var items = preview
            .Items.Select(item => new ColumnMappingPreviewDto(
                item.SourceColumnName,
                item.NormalizedSourceColumnName,
                item.TargetField,
                item.IsMapped,
                item.IsRequired
            ))
            .ToArray();

        return Result.Success(
            new PreviewColumnMappingResponse(
                file.OriginalFileName,
                file.SourceFileType.ToString(),
                preview.ProfileCode,
                items
            )
        );
    }
}
