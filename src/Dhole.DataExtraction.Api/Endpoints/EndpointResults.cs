using CustomCodeFramework.Api.Responses;
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;

namespace Dhole.DataExtraction.Api.Endpoints;

internal static class EndpointResults
{
    public static IResult FromResult<T>(Result<T> result, HttpContext httpContext)
    {
        return result.IsSuccess
            ? Results.Ok(ApiResponse<T>.Ok(result.Value))
            : Results.BadRequest(
                ApiErrorResponse.Create(
                    result.Error.Code,
                    result.Error.Message,
                    httpContext.TraceIdentifier
                )
            );
    }

    public static IResult FromResult(Result result, HttpContext httpContext)
    {
        return result.IsSuccess
            ? Results.NoContent()
            : Results.BadRequest(
                ApiErrorResponse.Create(
                    result.Error.Code,
                    result.Error.Message,
                    httpContext.TraceIdentifier
                )
            );
    }

    public static IResult Ok<T>(T value)
    {
        return Results.Ok(ApiResponse<T>.Ok(value));
    }

    public static IResult FromPaged<T>(PagedResult<T> result)
    {
        return Results.Ok(
            ApiPagedResponse<T>.Create(
                result.Items,
                result.PageNumber,
                result.PageSize,
                result.TotalCount
            )
        );
    }
}
