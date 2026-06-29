using System.Security.Cryptography;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.DataExtraction.Api.Extensions;
using Dhole.DataExtraction.Application.Extraction.DetectFileStructure;
using Dhole.DataExtraction.Application.Extraction.ExtractPricingData;
using Dhole.DataExtraction.Application.Extraction.PreviewColumnMapping;
using Dhole.DataExtraction.Application.Extraction.ValidatePricingData;
using Dhole.DataExtraction.Contracts.Extraction;

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
                var inputResult = await ReadFileAsync(request, httpContext, cancellationToken);

                if (inputResult.Error is not null)
                {
                    return inputResult.Error;
                }

                var input = inputResult.Input!;
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
                var inputResult = await ReadFileAsync(request, httpContext, cancellationToken);

                if (inputResult.Error is not null)
                {
                    return inputResult.Error;
                }

                var input = inputResult.Input!;
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
                var inputResult = await ReadFileAsync(request, httpContext, cancellationToken);

                if (inputResult.Error is not null)
                {
                    return inputResult.Error;
                }

                var input = inputResult.Input!;
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

        group.MapPost(
            "/extract",
            async (
                HttpRequest request,
                ICommandDispatcher dispatcher,
                HttpContext httpContext,
                CancellationToken cancellationToken
            ) =>
            {
                var inputResult = await ReadFileAsync(request, httpContext, cancellationToken);

                if (inputResult.Error is not null)
                {
                    return inputResult.Error;
                }

                var input = inputResult.Input!;
                var correlationId = httpContext.TraceIdentifier;
                var pricingImportId = Guid.NewGuid();

                var result = await dispatcher.DispatchAsync(
                    new ExtractPricingDataCommand(
                        new ExtractionDataRequest(
                            pricingImportId,
                            correlationId,
                            input.FileName,
                            input.ContentType,
                            Path.GetExtension(input.FileName),
                            input.Content.LongLength,
                            ComputeSha256(input.Content),
                            input.ProfileCode,
                            httpContext.GetCurrentUserId(),
                            httpContext.User.Identity?.Name,
                            input.Content
                        )
                    ),
                    cancellationToken
                );

                return EndpointResults.FromResult(result, httpContext);
            }
        );

        return app;
    }

    private static async Task<ReadFileResult> ReadFileAsync(
        HttpRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!request.HasFormContentType)
        {
            return ReadFileResult.Fail(
                BadRequest(
                    httpContext,
                    "invalid_content_type",
                    "La solicitud debe enviarse como multipart/form-data. En Postman o curl use el campo form-data llamado 'file'."
                )
            );
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

        if (file is null)
        {
            return ReadFileResult.Fail(
                BadRequest(
                    httpContext,
                    "missing_file",
                    "Debe adjuntar un archivo en multipart/form-data usando el campo 'file'."
                )
            );
        }

        if (file.Length == 0)
        {
            return ReadFileResult.Fail(
                BadRequest(
                    httpContext,
                    "empty_file",
                    "El archivo adjunto está vacío."
                )
            );
        }

        await using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        form.TryGetValue("profileCode", out var profileCode);

        return ReadFileResult.Success(
            new DevExtractionFileInput(
                file.FileName,
                file.ContentType,
                memoryStream.ToArray(),
                string.IsNullOrWhiteSpace(profileCode.ToString()) ? null : profileCode.ToString()
            )
        );
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
                title = "Invalid file upload",
                status = StatusCodes.Status400BadRequest,
                detail = message,
                instance = httpContext.Request.Path.Value,
                traceId = httpContext.TraceIdentifier,
                code,
            }
        );
    }

    private static string ComputeSha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed record DevExtractionFileInput(
        string FileName,
        string? ContentType,
        byte[] Content,
        string? ProfileCode
    );

    private sealed record ReadFileResult(
        DevExtractionFileInput? Input,
        IResult? Error
    )
    {
        public static ReadFileResult Success(DevExtractionFileInput input)
        {
            return new ReadFileResult(input, null);
        }

        public static ReadFileResult Fail(IResult error)
        {
            return new ReadFileResult(null, error);
        }
    }
}
