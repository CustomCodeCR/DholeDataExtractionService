using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.DataExtraction.Api.Extensions;
using Dhole.DataExtraction.Application.Extraction.DetectFileStructure;
using Dhole.DataExtraction.Application.Extraction.PreviewColumnMapping;
using Dhole.DataExtraction.Application.Extraction.ValidatePricingData;

namespace Dhole.DataExtraction.Api.Endpoints;

public static class DevExtractionTestEndpoints
{
    public static IEndpointRouteBuilder MapDevExtractionTestEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        var group = app.MapGroup("/api/dev/data-extraction")
            .WithTags("Dev Data Extraction")
            .AllowAnonymous();

        group.MapPost(
            "/detect-structure",
            async (
                HttpRequest request,
                IQueryDispatcher dispatcher,
                HttpContext httpContext,
                CancellationToken cancellationToken
            ) =>
            {
                var input = await ReadFileAsync(request, cancellationToken);
                var result = await dispatcher.DispatchAsync(
                    new DetectFileStructureQuery(
                        input.FileName,
                        input.ContentType,
                        input.Content,
                        input.ProfileCode
                    ),
                    cancellationToken
                );

                return EndpointResults.FromResult(result, httpContext);
            }
        );

        group.MapPost(
            "/preview-mapping",
            async (
                HttpRequest request,
                IQueryDispatcher dispatcher,
                HttpContext httpContext,
                CancellationToken cancellationToken
            ) =>
            {
                var input = await ReadFileAsync(request, cancellationToken);
                var result = await dispatcher.DispatchAsync(
                    new PreviewColumnMappingQuery(
                        input.FileName,
                        input.ContentType,
                        input.Content,
                        input.ProfileCode
                    ),
                    cancellationToken
                );

                return EndpointResults.FromResult(result, httpContext);
            }
        );

        group.MapPost(
            "/validate-pricing-data",
            async (
                HttpRequest request,
                IQueryDispatcher dispatcher,
                HttpContext httpContext,
                CancellationToken cancellationToken
            ) =>
            {
                var input = await ReadFileAsync(request, cancellationToken);
                var result = await dispatcher.DispatchAsync(
                    new ValidatePricingDataQuery(
                        input.FileName,
                        input.ContentType,
                        input.Content,
                        input.ProfileCode,
                        httpContext.GetCurrentUserId()
                    ),
                    cancellationToken
                );

                return EndpointResults.FromResult(result, httpContext);
            }
        );

        return app;
    }

    private static async Task<DevExtractionFileInput> ReadFileAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!request.HasFormContentType)
        {
            throw new InvalidOperationException(
                "La solicitud debe enviarse como multipart/form-data."
            );
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file =
            form.Files.FirstOrDefault()
            ?? throw new InvalidOperationException("Debe adjuntar un archivo.");

        await using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        form.TryGetValue("profileCode", out var profileCode);

        return new DevExtractionFileInput(
            file.FileName,
            file.ContentType,
            memoryStream.ToArray(),
            string.IsNullOrWhiteSpace(profileCode.ToString()) ? null : profileCode.ToString()
        );
    }

    private sealed record DevExtractionFileInput(
        string FileName,
        string? ContentType,
        byte[] Content,
        string? ProfileCode
    );
}
