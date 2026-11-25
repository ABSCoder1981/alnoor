using Microsoft.AspNetCore.Http.Features;

namespace ALNoor.Api.Shared.Web;

/// <summary>
/// Adds the stable machine-readable "code" field required by PRD section 16.1 to every ProblemDetails
/// response that does not already set one, based on the HTTP status.
/// </summary>
internal static class ProblemDetailsCodes
{
    public static IServiceCollection AddAlNoorProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            if (!problem.Extensions.ContainsKey("code"))
            {
                problem.Extensions["code"] = problem.Status switch
                {
                    StatusCodes.Status400BadRequest => "VALIDATION_FAILED",
                    StatusCodes.Status401Unauthorized => "UNAUTHENTICATED",
                    StatusCodes.Status403Forbidden => "FORBIDDEN",
                    StatusCodes.Status404NotFound => "NOT_FOUND",
                    StatusCodes.Status405MethodNotAllowed => "METHOD_NOT_ALLOWED",
                    StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
                    _ => "INTERNAL_ERROR",
                };
            }

            // Correlate client reports with server logs without exposing internals.
            problem.Extensions["traceId"] = context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToString()
                ?? context.HttpContext.TraceIdentifier;
        });
}
