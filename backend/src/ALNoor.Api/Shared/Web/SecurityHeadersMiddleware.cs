namespace ALNoor.Api.Shared.Web;

/// <summary>Baseline security headers for every API response (PRD section 21.1).</summary>
internal static class SecurityHeadersMiddleware
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                // The API serves JSON only, so nothing may be framed, scripted or embedded.
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                return Task.CompletedTask;
            });
            await next(context);
        });
}
