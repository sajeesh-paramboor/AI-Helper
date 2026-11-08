using Azure.Core;
using Azure.Identity;
using AI103.ChatApp.Infrastructure.Configuration;

namespace AI103.ChatApp.Infrastructure.Authentication;

/// <summary>
/// Builds the Entra ID <see cref="TokenCredential"/> chosen in configuration.
/// No keys or secrets are involved: each credential obtains short-lived access tokens for an identity.
/// </summary>
public static class TokenCredentialFactory
{
    public static TokenCredential Create(EntraAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tenantId = string.IsNullOrWhiteSpace(options.TenantId) ? null : options.TenantId;
        var managedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? null
            : options.ManagedIdentityClientId;

        return options.Mode switch
        {
            CredentialMode.AzureCli => new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }),

            // Always prompts in a browser for the configured tenant; never falls back to other cached logins.
            // The (optional) login hint only preselects the account on the Microsoft sign-in page.
            CredentialMode.InteractiveBrowser => new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = tenantId,
                LoginHint = string.IsNullOrWhiteSpace(options.LoginHint) ? null : options.LoginHint
            }),

            CredentialMode.ManagedIdentity => new ManagedIdentityCredential(
                managedIdentityClientId is null
                    ? ManagedIdentityId.SystemAssigned
                    : ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId)),

            CredentialMode.DefaultAzureCredential =>
                new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }),

            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unsupported credential mode.")
        };
    }
}
