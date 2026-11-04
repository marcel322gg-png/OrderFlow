using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace OrderFlow.API;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var duplicateValue = exception is DbUpdateException
        {
            InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        };
        var (status, title) = exception switch
        {
            ArgumentException => (400, "Invalid request"),
            KeyNotFoundException => (404, "Resource not found"),
            InvalidOperationException or DbUpdateConcurrencyException => (409, "Operation conflict"),
            _ when duplicateValue => (409, "Duplicate value"),
            _ => (500, "Unexpected error")
        };
        if (status == 500)
            logger.LogError(exception, "Request failed. TraceId={TraceId}", context.TraceIdentifier);
        else
            logger.LogInformation("Request rejected with {StatusCode}: {Message}", status, exception.Message);

        var detail = status == 500
            ? "An unexpected error occurred."
            : duplicateValue
                ? "A record with these unique values already exists."
                : exception is DbUpdateConcurrencyException
                    ? "The resource changed during this operation. Retry the request."
                    : exception.Message;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions = { ["traceId"] = context.TraceIdentifier }
        }, cancellationToken);
        return true;
    }
}
