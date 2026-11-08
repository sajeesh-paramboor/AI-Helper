using System.Text.Json;
using AI103.ChatApp.Application.DTOs;
using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AI103.ChatApp.Api.Controllers;

/// <summary>
/// Opt-in file search chat over documents that were indexed manually into a vector store.
/// Separate from <see cref="ChatController"/>, so the normal endpoints can never invoke the tool.
/// Disabled unless Foundry:FileSearch:Enabled is true. There is intentionally no upload or delete endpoint.
/// </summary>
[ApiController]
[Route("api/chat/file-search")]
public sealed class FileSearchChatController(
    IFileSearchConversationService conversations,
    IOptions<FileSearchOptions> options,
    ILogger<FileSearchChatController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web);

    /// <summary>Send a prompt with file search available and receive the answer plus the cited documents.</summary>
    [HttpPost("message")]
    [ProducesResponseType<FileSearchChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileSearchChatResponse>> SendMessage(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return DisabledProblem();
        }

        logger.LogInformation(
            "File search chat message received. Length={Length}, ConversationId={ConversationId}",
            request.Message.Length,
            request.ConversationId);

        return Ok(await conversations.SendAsync(request, cancellationToken));
    }

    /// <summary>Send a prompt with file search available and receive the answer incrementally as Server-Sent Events.</summary>
    /// <remarks>
    /// Events: <c>data: {"type":"delta","text":"..."}</c> repeated, then
    /// <c>data: {"type":"completed","responseId":"...","conversationId":"...","documents":[{"fileName":"...","fileId":"..."}]}</c>.
    /// If the model fails after streaming has started, <c>data: {"type":"error","message":"..."}</c> is sent instead.
    /// Documents are only known at the end of the stream.
    /// </remarks>
    [HttpPost("stream")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stream([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return DisabledProblem();
        }

        logger.LogInformation(
            "File search chat stream requested. Length={Length}, ConversationId={ConversationId}",
            request.Message.Length,
            request.ConversationId);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var update in conversations.StreamAsync(request, cancellationToken))
            {
                object payload = update.Kind switch
                {
                    FileSearchStreamUpdateKind.TextDelta => new { type = "delta", text = update.Text },
                    _ => new
                    {
                        type = "completed",
                        responseId = update.ResponseId,
                        conversationId = update.ConversationId,
                        documents = update.Documents
                    }
                };

                await WriteEventAsync(payload, cancellationToken);
            }
        }
        catch (Exception ex) when (Response.HasStarted && ex is not OperationCanceledException)
        {
            // Status 200 and headers are already sent, so an error can only be reported as an event.
            logger.LogError(ex, "File search streaming failed after the response started");
            await WriteEventAsync(
                new { type = "error", message = "The response was interrupted. Please try again." },
                CancellationToken.None);
        }

        return new EmptyResult();
    }

    private ObjectResult DisabledProblem() =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "File search is disabled",
            detail: "File search is not enabled on this server.");

    private async Task WriteEventAsync(object payload, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, SseJson)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
