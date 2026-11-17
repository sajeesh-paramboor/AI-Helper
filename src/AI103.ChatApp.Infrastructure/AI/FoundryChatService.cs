using System.Runtime.CompilerServices;
using AI103.ChatApp.Application.DTOs;
using AI103.ChatApp.Application.Exceptions;
using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Infrastructure.Configuration;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using System.ClientModel;

namespace AI103.ChatApp.Infrastructure.AI;

/// <summary>
/// <see cref="IChatService"/> backed by a model deployed in Azure AI Foundry, called through the
/// OpenAI .NET SDK Responses API. Authentication (Entra ID) is configured on the injected
/// <see cref="ResponsesClient"/>; this class never sees credentials.
/// </summary>
public sealed class FoundryChatService : IChatService
{
    private readonly ResponsesClient _client;
    private readonly FoundryOptions _options;
    private readonly ILogger<FoundryChatService> _logger;

    public FoundryChatService(
        ResponsesClient client,
        IOptions<FoundryOptions> options,
        ILogger<FoundryChatService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChatResult> SendMessageAsync(
        string message,
        string? previousResponseId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var request = BuildRequest(message, previousResponseId, stream: false);

        try
        {
            _logger.LogInformation(
                "Calling Responses API. Deployment={Deployment}, Chained={Chained}",
                _options.ModelDeploymentName, previousResponseId is not null);

            ClientResult<ResponseResult> result = await _client.CreateResponseAsync(request, cancellationToken);
            ResponseResult response = result.Value;

            LogUsage(response);

            if (response.Status is ResponseStatus.Failed or ResponseStatus.Cancelled)
            {
                throw new ChatServiceException(
                    $"The model did not complete the response (status: {response.Status}). {response.Error?.Message}");
            }

            var answer = response.GetOutputText();
            if (string.IsNullOrWhiteSpace(answer))
            {
                // A refusal is its own content kind and is not part of the plain output text.
                answer = RefusalText.Extract(response.OutputItems);
            }

            if (response.Status is ResponseStatus.Incomplete)
            {
                HandleIncomplete(response, hasText: !string.IsNullOrWhiteSpace(answer));
                if (string.IsNullOrWhiteSpace(answer))
                {
                    // e.g. a reasoning model used the whole token budget before producing visible text.
                    throw new ChatServiceException("The model stopped before producing an answer.");
                }
            }

            _logger.LogInformation("Responses API succeeded. ResponseId={ResponseId}", response.Id);
            return new ChatResult(answer, response.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ChatServiceException)
        {
            throw Translate(ex);
        }
    }

    public async IAsyncEnumerable<ChatStreamUpdate> StreamMessageAsync(
        string message,
        string? previousResponseId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var request = BuildRequest(message, previousResponseId, stream: true);

        _logger.LogInformation(
            "Opening Responses API stream. Deployment={Deployment}, Chained={Chained}",
            _options.ModelDeploymentName, previousResponseId is not null);

        AsyncStreamingResult<StreamingResponseUpdate> stream;
        try
        {
            stream = await _client.CreateResponseStreamingAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ChatServiceException)
        {
            throw Translate(ex);
        }

        // C# does not allow yield inside try/catch, so errors are surfaced by moving manually.
        // AsyncStreamingResult<T> implements IAsyncEnumerable<T> explicitly, hence the cast.
        await using var streamLifetime = stream;
        await using var enumerator = ((IAsyncEnumerable<StreamingResponseUpdate>)stream).GetAsyncEnumerator(cancellationToken);
        var textStreamed = false;
        var refusalDeltaSeen = false;
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    yield break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ChatServiceException)
            {
                throw Translate(ex);
            }

            switch (enumerator.Current)
            {
                case StreamingResponseOutputTextDeltaUpdate delta:
                    textStreamed |= !string.IsNullOrWhiteSpace(delta.Delta);
                    yield return ChatStreamUpdate.Delta(delta.Delta);
                    break;

                case StreamingResponseRefusalDeltaUpdate refusalDelta:
                    // A refusal is a separate content kind. It is shown to the user like normal text.
                    refusalDeltaSeen = true;
                    textStreamed |= !string.IsNullOrWhiteSpace(refusalDelta.Delta);
                    yield return ChatStreamUpdate.Delta(refusalDelta.Delta);
                    break;

                case StreamingResponseRefusalDoneUpdate refusalDone:
                    // The deltas already carried this text. Use the final text only if no delta arrived.
                    if (!refusalDeltaSeen && !string.IsNullOrWhiteSpace(refusalDone.Refusal))
                    {
                        textStreamed = true;
                        yield return ChatStreamUpdate.Delta(refusalDone.Refusal);
                    }

                    break;

                case StreamingResponseCompletedUpdate completed:
                    LogUsage(completed.Response);
                    _logger.LogInformation("Stream completed. ResponseId={ResponseId}", completed.Response.Id);
                    yield return ChatStreamUpdate.Complete(completed.Response.Id);
                    yield break;

                case StreamingResponseIncompleteUpdate incomplete:
                    // Stopped early (for example the output-token limit). Keep what was generated.
                    LogUsage(incomplete.Response);
                    HandleIncomplete(incomplete.Response, textStreamed);
                    yield return ChatStreamUpdate.Complete(incomplete.Response.Id);
                    yield break;

                case StreamingResponseFailedUpdate failed:
                    throw new ChatServiceException(
                        $"The model response failed. {failed.Response.Error?.Message}");

                case StreamingResponseErrorUpdate error:
                    throw new ChatServiceException($"The model stream reported an error. {error.Message}");
            }
        }
    }

    private CreateResponseOptions BuildRequest(string message, string? previousResponseId, bool stream)
    {
        var request = new CreateResponseOptions(
            _options.ModelDeploymentName,
            [ResponseItem.CreateUserMessageItem(message)])
        {
            // System-level behaviour, separate from the user's prompt.
            Instructions = _options.Instructions,
            // Chains this turn to the previous one server-side. Null starts a new conversation.
            PreviousResponseId = string.IsNullOrWhiteSpace(previousResponseId) ? null : previousResponseId,
            // The server must store responses for previous_response_id to work on the next turn.
            StoredOutputEnabled = _options.StoreResponses
        };

        if (stream)
        {
            request.StreamingEnabled = true;
        }

        return request;
    }

    /// <summary>
    /// Logs why a response stopped early. Hitting the output-token limit is not an error, because the partial
    /// answer is still usable. A content-filter stop is not an error either when the user already received text
    /// (typically the safety refusal); it is an error only when no text was produced, so an empty answer is
    /// never reported as a success.
    /// </summary>
    private void HandleIncomplete(ResponseResult response, bool hasText)
    {
        var reason = response.IncompleteStatusDetails?.Reason;
        _logger.LogWarning("Response incomplete. ResponseId={ResponseId}, Reason={Reason}", response.Id, reason);

        if (reason == ResponseIncompleteStatusReason.ContentFilter)
        {
            if (!hasText)
            {
                throw new ChatServiceException("The response was stopped by the content filter.");
            }

            _logger.LogWarning(
                "Content filter stopped the response after text was produced; treating the turn as completed. ResponseId={ResponseId}",
                response.Id);
        }
    }

    /// <summary>Logs token counts only. Prompt and answer text are never logged.</summary>
    private void LogUsage(ResponseResult response)
    {
        if (response.Usage is { } usage)
        {
            _logger.LogInformation(
                "Token usage. ResponseId={ResponseId}, Input={Input}, Output={Output}, Reasoning={Reasoning}, Total={Total}",
                response.Id,
                usage.InputTokenCount,
                usage.OutputTokenCount,
                usage.OutputTokenDetails?.ReasoningTokenCount,
                usage.TotalTokenCount);
        }
    }

    /// <summary>
    /// Converts SDK/Azure exceptions to a provider-neutral exception. Messages are kept generic;
    /// prompt content is never included.
    /// </summary>
    private ChatServiceException Translate(Exception ex)
    {
        switch (ex)
        {
            case ClientResultException cre:
                _logger.LogError(cre, "Model service returned HTTP {Status}", cre.Status);
                return new ChatServiceException(
                    $"The model service returned HTTP {cre.Status}.", cre.Status, cre);

            case AuthenticationFailedException or CredentialUnavailableException:
                _logger.LogError(ex, "Entra ID authentication failed");
                return new ChatServiceException(
                    "Authentication with Microsoft Entra ID failed. Check the signed-in identity and its RBAC role.",
                    StatusCodes.Unauthorized, ex);

            case RequestFailedException rfe:
                _logger.LogError(rfe, "Azure request failed with HTTP {Status}", rfe.Status);
                return new ChatServiceException(
                    $"An Azure request failed with HTTP {rfe.Status}.", rfe.Status, rfe);

            default:
                _logger.LogError(ex, "Unexpected error calling the model service");
                return new ChatServiceException("An unexpected error occurred calling the model service.", null, ex);
        }
    }

    private static class StatusCodes
    {
        public const int Unauthorized = 401;
    }
}
