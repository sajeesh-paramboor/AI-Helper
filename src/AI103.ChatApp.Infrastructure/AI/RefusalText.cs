using System.Text;
using OpenAI.Responses;

namespace AI103.ChatApp.Infrastructure.AI;

/// <summary>
/// Reads model refusal text from Responses API output items. A refusal is a content part of its own kind,
/// so it is not part of the plain output text. Pure and side-effect free.
/// </summary>
internal static class RefusalText
{
    public static string Extract(IEnumerable<ResponseItem> outputItems)
    {
        ArgumentNullException.ThrowIfNull(outputItems);

        var refusal = new StringBuilder();
        foreach (var item in outputItems)
        {
            if (item is not MessageResponseItem { Role: MessageRole.Assistant } message)
            {
                continue;
            }

            foreach (var part in message.Content)
            {
                if (part.Kind == ResponseContentPartKind.Refusal && !string.IsNullOrWhiteSpace(part.Refusal))
                {
                    refusal.Append(part.Refusal);
                }
            }
        }

        return refusal.ToString();
    }
}
