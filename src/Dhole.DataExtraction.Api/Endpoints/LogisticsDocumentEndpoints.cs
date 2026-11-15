using System.Security.Cryptography;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Infrastructure.Documentation;
using Dhole.DataExtraction.Infrastructure.Markdown;

namespace Dhole.DataExtraction.Api.Endpoints;

public static class LogisticsDocumentEndpoints
{
    private const long MaximumBytes = 20 * 1024 * 1024;

    public static IEndpointRouteBuilder MapLogisticsDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/data-extraction/documents/analyze", AnalyzeAsync)
            .WithTags("Logistics Documents")
            .RequireAuthorization()
            .DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> AnalyzeAsync(
        HttpRequest request,
        IAiEmailContentReader contentReader,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { code = "DataExtraction.MultipartRequired" });

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length <= 0 || file.Length > MaximumBytes)
            return Results.BadRequest(new { code = "DataExtraction.InvalidDocumentSize",
                maximumBytes = MaximumBytes });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".xlsx" or ".xls" or ".docx" or ".doc"
            or ".txt" or ".csv" or ".html" or ".htm" or ".eml" or ".msg"
            or ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp" or ".webp"))
            return Results.BadRequest(new { code = "DataExtraction.UnsupportedDocumentFormat" });

        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var text = await contentReader.ReadAsTextAsync(
            file.FileName, file.ContentType, ext, bytes, cancellationToken);
        var evidence = LogisticsDocumentEvidenceExtractor.Analyze(text, file.FileName);

        // Data is never sent to Pricing or customs authorities from this endpoint.
        // Downstream consumers must explicitly review the source-backed evidence.
        return Results.Ok(new
        {
            sourceFileName = Path.GetFileName(file.FileName),
            sourceSha256 = hash,
            evidence.DocumentType,
            evidence.Fields,
            evidence.Warnings,
            evidence.RequiresHumanReview,
            extractedText = text,
            reviewStatus = "PendingReview"
        });
    }
}
