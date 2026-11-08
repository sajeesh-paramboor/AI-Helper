using System.ComponentModel.DataAnnotations;

namespace AI103.ChatApp.Infrastructure.Configuration;

/// <summary>
/// Settings that locate and shape the Azure AI Foundry model. Bound from the "Foundry" section.
/// All values are supplied by you after creating the resources manually; none are hardcoded.
/// </summary>
public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";

    /// <summary>
    /// Foundry project endpoint. OPTIONAL: not used by the OpenAI SDK code path. It is the entry
    /// point for the Foundry SDK (Azure.AI.Projects), kept here for comparison and future use.
    /// </summary>
    [Url(ErrorMessage = "Foundry:ProjectEndpoint must be a valid URL when set.")]
    public string? ProjectEndpoint { get; set; }

    /// <summary>
    /// OpenAI-compatible (v1) endpoint that serves the Responses API.
    /// This is the base address the OpenAI SDK sends requests to.
    /// </summary>
    [Required(ErrorMessage = "Foundry:OpenAIEndpoint is required.")]
    [Url(ErrorMessage = "Foundry:OpenAIEndpoint must be a valid http(s) URL. Replace the placeholder with your real endpoint.")]
    public string OpenAIEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The NAME you gave the model deployment (not the underlying model name). Sent as the "model" field.
    /// </summary>
    [Required(ErrorMessage = "Foundry:ModelDeploymentName is required.")]
    [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9._\-]*$",
        ErrorMessage = "Foundry:ModelDeploymentName looks invalid. Replace the placeholder with your deployment name.")]
    public string ModelDeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// OAuth scope requested when acquiring the Entra ID access token (for example "https://.../.default").
    /// Must match the audience of the endpoint above; confirmed during Phase 7.
    /// </summary>
    [Required(ErrorMessage = "Foundry:TokenScope is required.")]
    [RegularExpression(@"^https://\S+/\.default$",
        ErrorMessage = "Foundry:TokenScope must look like 'https://<audience>/.default'. Replace the placeholder.")]
    public string TokenScope { get; set; } = string.Empty;

    /// <summary>
    /// System-level instructions sent on every request. Define the assistant's behaviour here.
    /// </summary>
    [Required]
    public string Instructions { get; set; } = "You are a helpful assistant.";

    /// <summary>
    /// Whether the service stores responses so they can be referenced by previous_response_id.
    /// Must stay true for response chaining to work.
    /// </summary>
    public bool StoreResponses { get; set; } = true;
}
