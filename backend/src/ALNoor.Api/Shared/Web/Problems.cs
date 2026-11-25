using Microsoft.AspNetCore.Http.HttpResults;

namespace ALNoor.Api.Shared.Web;

/// <summary>ProblemDetails responses with the stable machine-readable codes from PRD section 16.</summary>
internal static class Problems
{
    public static ProblemHttpResult Create(int status, string code, string title, IDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                extensions[key] = value;
            }
        }

        return TypedResults.Problem(statusCode: status, title: title, extensions: extensions);
    }

    public static ProblemHttpResult NotFound() =>
        Create(StatusCodes.Status404NotFound, "NOT_FOUND", "The resource was not found.");

    public static ProblemHttpResult Forbidden(string code = "FORBIDDEN", string title = "You are not allowed to do this.") =>
        Create(StatusCodes.Status403Forbidden, code, title);

    public static ProblemHttpResult Conflict(string code, string title) =>
        Create(StatusCodes.Status409Conflict, code, title);

    public static ValidationProblem Validation(string field, string message) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [message] },
            extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
