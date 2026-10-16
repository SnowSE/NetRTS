using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using NetRts.Protocol;
using NetRts.Server.Matches;

namespace NetRts.Server.Endpoints;

public static class ApiErrors
{
    /// <summary>Largest request body the server accepts (Kestrel's limit too): far more than any real command batch.</summary>
    public const int MaxRequestBodyBytes = 256 * 1024;

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new ApiErrorDto { Code = code, Message = message }, statusCode: status);

    /// <summary>Documents the uniform error body for the 4xx responses an endpoint can return.</summary>
    public static RouteHandlerBuilder ProducesErrors(this RouteHandlerBuilder builder) =>
        builder.Produces<ApiErrorDto>(400).Produces<ApiErrorDto>(401).Produces<ApiErrorDto>(403)
               .Produces<ApiErrorDto>(404).Produces<ApiErrorDto>(409).Produces<ApiErrorDto>(429);

    public static IResult NotFound(string what) => Error(404, "NOT_FOUND", $"{what} not found.");

    /// <summary>Turns exceptions into the uniform { code, message } error body; never leaks stack traces.</summary>
    public static void UseApiErrors(this WebApplication app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            // Bad requests from bots are expected traffic, not server faults: don't log them as errors.
            SuppressDiagnosticsCallback = context => context.Exception is MatchException or BadHttpRequestException,
            ExceptionHandler = async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var (status, code, message) = exception switch
                {
                    MatchException m => (m.Status, m.Code, m.Message),
                    BadHttpRequestException { InnerException: JsonException json } => (400, "INVALID_JSON", json.Message),
                    BadHttpRequestException bad => (bad.StatusCode, "BAD_REQUEST", bad.Message),
                    _ => (500, "INTERNAL_ERROR", "Something went wrong on the server."),
                };

                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new ApiErrorDto { Code = code, Message = message });
            },
        });

        // Kestrel answers an oversized body with an empty 413 (minimal APIs swallow the read error), which
        // leaves bots guessing. Refuse it up front, before anything reads the body, with the usual error body.
        app.Use(async (context, next) =>
        {
            if (context.Request.ContentLength > MaxRequestBodyBytes)
            {
                await Error(413, "REQUEST_TOO_LARGE",
                    $"Request bodies are limited to {MaxRequestBodyBytes / 1024} KB; this one is {context.Request.ContentLength / 1024} KB.")
                    .ExecuteAsync(context);
                return;
            }
            await next(context);
        });
    }
}
