using Microsoft.AspNetCore.Diagnostics;

namespace AlteredOwnership.Server.Infrastructure.Hosting;

// Guarantees every unhandled exception gets one unmissable Error-level log line
// with the full exception and request context, before ProblemDetails writes the
// generic response body the client sees. Returns false so the built-in
// ProblemDetails handler (registered via AddProblemDetails()) still runs — this
// only observes, it doesn't take over the response.
public class UnhandledExceptionLogger(ILogger<UnhandledExceptionLogger> logger) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception,
            "Unhandled exception on {Method} {Path} (traceId {TraceId})",
            httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);
        return ValueTask.FromResult(false);
    }
}
