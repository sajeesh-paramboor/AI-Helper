using System.ComponentModel.DataAnnotations;

namespace AI103.ChatApp.Infrastructure.Configuration;

/// <summary>
/// Which Entra ID credential the app uses to call Azure. Chosen explicitly so a stray cached
/// login on the machine is never picked up silently.
/// </summary>
public enum CredentialMode
{
    /// <summary>
    /// Opens a browser sign-in for the account you choose, in an explicitly configured tenant.
    /// Does NOT reuse Azure CLI, Visual Studio or other cached logins. Local development.
    /// </summary>
    InteractiveBrowser,

    /// <summary>Uses the account currently signed in to Azure CLI ("az login"). Local development alternative.</summary>
    AzureCli,

    /// <summary>Azure managed identity (system-assigned, or user-assigned via ManagedIdentityClientId). Azure deployment.</summary>
    ManagedIdentity,

    /// <summary>The full DefaultAzureCredential chain. Convenient, but may pick a different account than you expect.</summary>
    DefaultAzureCredential
}

/// <summary>
/// Entra ID authentication settings. Bound from the "EntraAuth" section. Contains no secrets.
/// </summary>
public sealed class EntraAuthOptions : IValidatableObject
{
    public const string SectionName = "EntraAuth";

    private const string GuidPattern = @"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$";

    public CredentialMode Mode { get; set; } = CredentialMode.InteractiveBrowser;

    /// <summary>
    /// Directory (tenant) ID to sign in to. Required for InteractiveBrowser so the sign-in always
    /// targets the tenant that owns your subscription and model deployment.
    /// </summary>
    [RegularExpression(GuidPattern, ErrorMessage = "EntraAuth:TenantId must be a GUID, or empty. Replace the placeholder.")]
    public string? TenantId { get; set; }

    /// <summary>
    /// Optional. Account (sign-in name) to preselect in the browser, e.g. your personal Microsoft account.
    /// It is only a hint: you still sign in yourself. Keep your real value out of Git (use user-secrets).
    /// </summary>
    public string? LoginHint { get; set; }

    /// <summary>Client ID of a user-assigned managed identity. Leave empty for system-assigned. Optional.</summary>
    [RegularExpression(GuidPattern, ErrorMessage = "EntraAuth:ManagedIdentityClientId must be a GUID, or empty. Replace the placeholder.")]
    public string? ManagedIdentityClientId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Mode == CredentialMode.InteractiveBrowser && string.IsNullOrWhiteSpace(TenantId))
        {
            yield return new ValidationResult(
                "EntraAuth:TenantId is required when EntraAuth:Mode is InteractiveBrowser.",
                [nameof(TenantId)]);
        }
    }
}
