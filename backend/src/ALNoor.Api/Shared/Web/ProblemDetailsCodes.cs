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

            // PRD 16.1: JSON is camelCase, including validation error keys ("Schedule[0].StartTimeLocal" -> "schedule[0].startTimeLocal").
            if (problem is HttpValidationProblemDetails validation && validation.Errors.Count > 0)
            {
                var camel = validation.Errors.ToDictionary(e => ToCamelPath(e.Key), e => e.Value, StringComparer.Ordinal);
                validation.Errors.Clear();
                foreach (var (key, value) in camel)
                {
                    validation.Errors[key] = value;
                }
            }

            // Correlate client reports with server logs without exposing internals.
            problem.Extensions["traceId"] = context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToString()
                ?? context.HttpContext.TraceIdentifier;
        });

    internal static string ToCamelPath(string path) =>
        string.Join('.', path.Split('.').Select(segment =>
            segment.Length == 0 || char.IsLower(segment[0])
                ? segment
                : string.Concat(char.ToLowerInvariant(segment[0]).ToString(), segment.AsSpan(1))));
}
