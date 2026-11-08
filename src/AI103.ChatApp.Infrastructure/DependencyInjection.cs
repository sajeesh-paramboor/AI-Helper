using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Infrastructure.AI;
using AI103.ChatApp.Infrastructure.Authentication;
using AI103.ChatApp.Infrastructure.Configuration;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using System.ClientModel.Primitives;

namespace AI103.ChatApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Azure AI Foundry integration: validated options, the Entra ID credential,
    /// the OpenAI SDK <see cref="ResponsesClient"/> and <see cref="FoundryChatService"/>.
    /// NOT called from Program.cs yet; it is enabled in Phase 8 after the manual Azure setup.
    /// </summary>
    public static IServiceCollection AddFoundryChatService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FoundryOptions>()
            .Bind(configuration.GetSection(FoundryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EntraAuthOptions>()
            .Bind(configuration.GetSection(EntraAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TokenCredential>(sp =>
            TokenCredentialFactory.Create(sp.GetRequiredService<IOptions<EntraAuthOptions>>().Value));

        // ResponsesClient is thread-safe and intended to be reused, so register it as a singleton.
        services.AddSingleton(sp =>
        {
            var foundry = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;
            var credential = sp.GetRequiredService<TokenCredential>();

            // The trailing slash matters: without it, URI composition can drop the last path segment.
            var endpoint = foundry.OpenAIEndpoint.EndsWith('/') ? foundry.OpenAIEndpoint : foundry.OpenAIEndpoint + "/";

            // BearerTokenPolicy fetches/refreshes Entra ID tokens and adds "Authorization: Bearer <token>".
            // No API key is used anywhere.
            return new ResponsesClient(
                new BearerTokenPolicy(credential, foundry.TokenScope),
                new ResponsesClientOptions { Endpoint = new Uri(endpoint) });
        });

        services.AddSingleton<IChatService, FoundryChatService>();
        return services;
    }
}
