using System.Runtime.CompilerServices;
using System.Text;
using AI103.ChatApp.Application.DTOs;
using AI103.ChatApp.Application.Exceptions;
using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AI103.ChatApp.Application.Services;

/// <summary>
/// Same conversation rules as <see cref="ConversationService"/> (conversationId wins over previousResponseId,
/// unknown ID is a 404, only successful turns are recorded), but each turn runs with web search enabled.
/// The existing service is deliberately left untouched.
/// </summary>
public sealed class WebSearchConversationService(
    IWebSearchChatService chatService,
    IConversationStore store,
    ILogger<WebSearchConversationService> logger) : IWebSearchConversationService
{
    public async Task<WebSearchChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conversation = await ResolveAsync(request, cancellationToken);

        var result = await chatService.SendWithWebSearchAsync(
            request.Message, conversation.LatestResponseId, cancellationToken);

        await RecordAsync(conversation, request.Message, result.Answer, result.ResponseId, cancellationToken);
        return new WebSearchChatResponse(result.Answer, result.ResponseId, conversation.Id, result.Sources);
    }

    public async IAsyncEnumerable<WebSearchStreamUpdate> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conversation = await ResolveAsync(request, cancellationToken);
        var answer = new StringBuilder();

        await foreach (var update in chatService.StreamWithWebSearchAsync(
                           request.Message, conversation.LatestResponseId, cancellationToken))
        {
            if (update.Kind == WebSearchStreamUpdateKind.TextDelta)
            {
                answer.Append(update.Text);
                yield return update;
            }
            else if (update.Kind == WebSearchStreamUpdateKind.Completed)
            {
                // The turn is saved only after the model reports completion. The text is recorded as streamed.
                await RecordAsync(conversation, request.Message, answer.ToString(), update.ResponseId!, cancellationToken);
                yield return WebSearchStreamUpdate.Complete(
                    update.ResponseId!, update.Sources ?? [], conversation.Id);
            }
        }
    }

    private async Task RecordAsync(
        Conversation conversation, string userText, string answer, string responseId, CancellationToken cancellationToken)
    {
        conversation.RecordTurn(
            ChatMessage.Create(ChatRole.User, userText),
            ChatMessage.Create(ChatRole.Assistant, answer),
            responseId);
        await store.SaveAsync(conversation, cancellationToken);
    }

    private async Task<Conversation> ResolveAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ConversationId))
        {
            return await store.GetAsync(request.ConversationId, cancellationToken)
                   ?? throw new ConversationNotFoundException(request.ConversationId);
        }

        var conversation = new Conversation();
        if (!string.IsNullOrWhiteSpace(request.PreviousResponseId))
        {
            conversation.RecordResponseId(request.PreviousResponseId);
        }

        logger.LogInformation("Started conversation {ConversationId} (web search turn)", conversation.Id);
        return conversation;
    }
}
