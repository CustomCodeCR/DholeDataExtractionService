using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Contracts.Tabular;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Files;

namespace Dhole.DataExtraction.Api.Endpoints;

public static class TabularExtractionEndpoints
{
    private const int DefaultMaximumRows = 2000;
    private const int AbsoluteMaximumRows = 5000;

    public static IEndpointRouteBuilder MapTabularExtractionEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        var group = app
            .MapGroup("/api/data-extraction/tabular")
            .WithTags("Tabular Extraction")
            .RequireAuthorization();

        group.MapPost("/extract", ExtractAsync).DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> ExtractAsync(
        HttpRequest request,
        IDocumentExtractorFactory extractorFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!request.HasFormContentType)
        {
            return BadRequest(
                httpContext,
                "DataExtraction.InvalidContentType",
                "La solicitud debe enviarse como multipart/form-data."
            );
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

        if (file is null)
        {
            return BadRequest(
                httpContext,
                "DataExtraction.MissingFile",
                "Debe adjuntar un archivo CSV o XLSX en el campo 'file'."
            );
        }

        if (file.Length <= 0)
        {
            return BadRequest(
                httpContext,
                "DataExtraction.EmptyFile",
                "El archivo adjunto está vacío."
            );
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx"))
        {
            return BadRequest(
                httpContext,
                "DataExtraction.UnsupportedTabularFile",
                "Solo se permiten archivos CSV o XLSX."
            );
        }

        await using var inputStream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await inputStream.CopyToAsync(memoryStream, cancellationToken);
        var content = memoryStream.ToArray();

        var fileType = FileTypeDetector.Detect(file.FileName, file.ContentType, content);
        if (fileType is not (SourceFileType.Excel or SourceFileType.Csv))
        {
            return BadRequest(
                httpContext,
                "DataExtraction.UnsupportedTabularFile",
                "Solo se permiten archivos CSV o XLSX."
            );
        }

        if (!extractorFactory.CanExtract(fileType))
        {
            return BadRequest(
                httpContext,
                "DataExtraction.ExtractorUnavailable",
                "No existe un extractor configurado para el archivo adjunto."
            );
        }

        var maximumRows = DefaultMaximumRows;
        if (
            form.TryGetValue("maximumRows", out var maximumRowsValue)
            && int.TryParse(maximumRowsValue.ToString(), out var parsedMaximumRows)
        )
        {
            maximumRows = Math.Clamp(parsedMaximumRows, 1, AbsoluteMaximumRows);
        }

        ExtractedDocument document;
        try
        {
            document = await extractorFactory
                .GetExtractor(fileType)
                .ExtractAsync(
                    new DocumentExtractionInput(
                        file.FileName,
                        file.ContentType,
                        Path.GetExtension(file.FileName),
                        content
                    ),
                    cancellationToken
                );
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return BadRequest(
                httpContext,
                "DataExtraction.TabularExtractionFailed",
                $"No fue posible leer el archivo: {exception.Message}"
            );
        }

        var totalRows = document.Tables.Sum(table => table.Rows.Count);
        var remainingRows = maximumRows;
        var sheets = new List<TabularSheetDto>();

        foreach (var table in document.Tables)
        {
            var rows = table.Rows
                .Take(Math.Max(0, remainingRows))
                .Select(row => new TabularRowDto(row.RowNumber, row.Values))
                .ToArray();

            remainingRows -= rows.Length;

            sheets.Add(
                new TabularSheetDto(
                    table.SheetName,
                    table.Headers.ToArray(),
                    rows
                )
            );
        }

        var includedRows = sheets.Sum(sheet => sheet.Rows.Count);
        var result = new TabularExtractionDto(
            document.OriginalFileName,
            document.FileType.ToString(),
            sheets,
            totalRows,
            includedRows,
            includedRows < totalRows
        );

        return EndpointResults.Ok(result);
    }

    private static IResult BadRequest(
        HttpContext httpContext,
        string code,
        string message
    )
    {
        return Results.BadRequest(
            new
            {
                title = "Tabular extraction error",
                status = StatusCodes.Status400BadRequest,
                detail = message,
                instance = httpContext.Request.Path.Value,
                traceId = httpContext.TraceIdentifier,
                code,
            }
        );
    }
}
