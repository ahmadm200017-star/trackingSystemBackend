using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MdfTracker.Api.Middleware;

/// <summary>
/// Turns a database outage into a 503 that says so, instead of the generic 500 the default
/// handler produces.
///
/// This matters for a client that cannot see the server logs: a 500 is indistinguishable
/// from a bug in the request, whereas a 503 with Retry-After tells the device to hold the
/// data and try again. The exception detail is logged, never returned - a connection string
/// and server name are exactly what a SqlException message tends to contain.
/// </summary>
public class DatabaseFailureHandler : IExceptionHandler
{
    private const int RetryAfterSeconds = 10;

    private readonly ILogger<DatabaseFailureHandler> _logger;

    public DatabaseFailureHandler(ILogger<DatabaseFailureHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!IsDatabaseFailure(exception))
        {
            // Not ours; let the default pipeline turn it into a 500.
            return false;
        }

        _logger.LogError(exception, "Database unavailable handling {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();

        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Database unavailable",
            Detail = "The database could not be reached. The request was not saved; retry shortly."
        }, cancellationToken);

        return true;
    }

    /// <summary>
    /// Walks the inner exceptions: EF wraps provider errors, so a SqlException usually
    /// arrives nested inside a DbUpdateException or an InvalidOperationException.
    /// </summary>
    private static bool IsDatabaseFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
