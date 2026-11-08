using AI103.ChatApp.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AI103.ChatApp.Api;

/// <summary>
/// Turns unhandled exceptions into RFC 9457 ProblemDetails responses. Internal details (stack traces,
/// upstream error bodies, request headers) are logged on the server and never returned to the caller.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to respond to.
            logger.LogInformation("Request cancelled by the client");
            return true;
        }

        var (status, title, detail) = exception switch
        {
            ConversationNotFoundException => (
                StatusCodes.Status404NotFound,
                "Conversation not found",
                "The conversation does not exist or has expired. Start a new conversation."),

            ChatServiceException { UpstreamStatusCode: StatusCodes.Status429TooManyRequests } => (
                StatusCodes.Status429TooManyRequests,
                "The model service is busy",
                "The model service is receiving too many requests. Please try again shortly."),

            ChatServiceException => (
                StatusCodes.Status502BadGateway,
                "The model service failed",
                "The assistant could not get an answer from the model service. Please try again."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                "Something went wrong. Please try again.")
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception. Responding {Status}", status);
        }
        else
        {
            logger.LogWarning("Request failed with {Status}: {Reason}", status, exception.Message);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}
