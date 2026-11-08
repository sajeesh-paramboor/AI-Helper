using System.Text.Json;
using AI103.ChatApp.Application.DTOs;
using AI103.ChatApp.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AI103.ChatApp.Api.Controllers;

/// <summary>
/// Thin HTTP adapter: validates input (via [ApiController]), calls IConversationService, maps to HTTP.
/// No AI or Azure logic lives here. Unhandled exceptions are mapped to ProblemDetails by ApiExceptionHandler.
/// </summary>
[ApiController]
[Route("api/chat")]
public sealed class ChatController(IConversationService conversations, ILogger<ChatController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web);

    /// <summary>Send a prompt and receive the complete answer.</summary>
    [HttpPost("message")]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatResponse>> SendMessage(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Chat message received. Length={Length}, ConversationId={ConversationId}",
            request.Message.Length,
            request.ConversationId);

        return Ok(await conversations.SendAsync(request, cancellationToken));
    }

    /// <summary>Send a prompt and receive the answer incrementally as Server-Sent Events.</summary>
    /// <remarks>
    /// Events: <c>data: {"type":"delta","text":"..."}</c> repeated, then
    /// <c>data: {"type":"completed","responseId":"...","conversationId":"..."}</c>.
    /// If the model fails after streaming has started, <c>data: {"type":"error","message":"..."}</c> is sent instead.
    /// </remarks>
    [HttpPost("stream")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task Stream([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Chat stream requested. Length={Length}, ConversationId={ConversationId}",
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
                    ChatStreamUpdateKind.TextDelta => new { type = "delta", text = update.Text },
                    _ => new { type = "completed", responseId = update.ResponseId, conversationId = update.ConversationId }
                };

                await WriteEventAsync(payload, cancellationToken);
            }
        }
        catch (Exception ex) when (Response.HasStarted && ex is not OperationCanceledException)
        {
            // Status 200 and headers are already sent, so an error can only be reported as an event.
            logger.LogError(ex, "Streaming failed after the response started");
            await WriteEventAsync(
                new { type = "error", message = "The response was interrupted. Please try again." },
                CancellationToken.None);
        }
    }

    /// <summary>Returns the messages of a conversation.</summary>
    [HttpGet("conversations/{conversationId}")]
    [ProducesResponseType<ConversationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> GetConversation(
        string conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken);
        return conversation is null ? NotFound() : Ok(conversation);
    }

    /// <summary>API liveness check. Does not call Azure.</summary>
    [HttpGet("health")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Health() =>
        Ok(new HealthResponse("Healthy", DateTimeOffset.UtcNow));

    private async Task WriteEventAsync(object payload, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, SseJson)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
