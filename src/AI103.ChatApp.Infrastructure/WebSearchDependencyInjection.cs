using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Infrastructure.AI;
using AI103.ChatApp.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AI103.ChatApp.Infrastructure;

public static class WebSearchDependencyInjection
{
    /// <summary>
    /// Registers the optional web search capability. Requires <see cref="DependencyInjection.AddFoundryChatService"/>
    /// to have been called first, because it reuses the registered ResponsesClient and FoundryOptions.
    /// </summary>
    public static IServiceCollection AddWebSearchChatService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WebSearchOptions>().Bind(configuration.GetSection(WebSearchOptions.SectionName));
        services.AddSingleton<IWebSearchChatService, WebSearchFoundryChatService>();
        return services;
    }
}
