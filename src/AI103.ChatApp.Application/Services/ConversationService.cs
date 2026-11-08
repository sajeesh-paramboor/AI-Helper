using System.Runtime.CompilerServices;
using System.Text;
using AI103.ChatApp.Application.DTOs;
using AI103.ChatApp.Application.Exceptions;
using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AI103.ChatApp.Application.Services;

/// <summary>
/// Keeps conversation state on the server. The caller only holds a conversation ID; this service
/// remembers the provider's latest response ID and chains each turn to it with previous_response_id.
/// Only successful turns are recorded, so a failed or cancelled turn leaves the conversation unchanged.
/// </summary>
public sealed class ConversationService(
    IChatService chatService,
    IConversationStore store,
    ILogger<ConversationService> logger) : IConversationService
{
    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conversation = await ResolveAsync(request, cancellationToken);

        var result = await chatService.SendMessageAsync(request.Message, conversation.LatestResponseId, cancellationToken);

        await RecordAsync(conversation, request.Message, result.Answer, result.ResponseId, cancellationToken);
        return new ChatResponse(result.Answer, result.ResponseId, conversation.Id);
    }

    public async IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conversation = await ResolveAsync(request, cancellationToken);
        var answer = new StringBuilder();

        await foreach (var update in chatService.StreamMessageAsync(
                           request.Message, conversation.LatestResponseId, cancellationToken))
        {
            if (update.Kind == ChatStreamUpdateKind.TextDelta)
            {
                answer.Append(update.Text);
                yield return update;
            }
            else if (update.Kind == ChatStreamUpdateKind.Completed)
            {
                await RecordAsync(conversation, request.Message, answer.ToString(), update.ResponseId!, cancellationToken);
                yield return ChatStreamUpdate.Complete(update.ResponseId!, conversation.Id);
            }
        }
    }

    public async Task<ConversationDto?> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await store.GetAsync(conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        return new ConversationDto(
            conversation.Id,
            conversation.CreatedAtUtc,
            conversation.Messages
                .Select(m => new ConversationMessageDto(m.Role.ToString().ToLowerInvariant(), m.Content, m.CreatedAtUtc))
                .ToArray());
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
            // The caller is continuing a chain this server is not tracking yet.
            conversation.RecordResponseId(request.PreviousResponseId);
        }

        logger.LogInformation("Started conversation {ConversationId}", conversation.Id);
        return conversation;
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
}
