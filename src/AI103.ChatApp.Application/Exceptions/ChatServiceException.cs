namespace AI103.ChatApp.Application.Exceptions;

/// <summary>
/// Provider-neutral failure raised by an <see cref="Interfaces.IChatService"/> implementation.
/// Lets the API layer map errors to HTTP without referencing the OpenAI or Azure SDKs.
/// </summary>
public sealed class ChatServiceException : Exception
{
    public ChatServiceException(string message, int? upstreamStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
    }

    /// <summary>HTTP status returned by the model service, when the failure came from an HTTP response.</summary>
    public int? UpstreamStatusCode { get; }
}
