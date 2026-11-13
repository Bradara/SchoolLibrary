namespace SchoolLibrary.Middleware;

/// <summary>
/// Adds response headers that make browsers refuse some attacks. They are set on every response,
/// including static files and error pages.
/// </summary>
public class SecurityHeadersMiddleware
{
    /// <summary>
    /// Only the directives that cannot break an embedded resource or the inline scripts of the pages:
    /// no other site may show this one in a frame (clickjacking), no plug-ins, no changed base address,
    /// and forms can only be sent to this site. The resources themselves are shown in iframes of this
    /// site, which these directives do not restrict.
    /// </summary>
    public const string ContentSecurityPolicy =
        "frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // The browser must not guess a file type (a text file shown as a script)
        headers["X-Content-Type-Options"] = "nosniff";

        // Older browsers know X-Frame-Options but not frame-ancestors
        headers["X-Frame-Options"] = "DENY";

        // The same value the embedded resources are loaded with (the iframes ask for it explicitly)
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;

        return _next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
