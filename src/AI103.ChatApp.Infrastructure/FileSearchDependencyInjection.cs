using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Infrastructure.AI;
using AI103.ChatApp.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AI103.ChatApp.Infrastructure;

public static class FileSearchDependencyInjection
{
    /// <summary>
    /// Registers the optional file search capability. Requires <see cref="DependencyInjection.AddFoundryChatService"/>
    /// to have been called first, because it reuses the registered ResponsesClient and FoundryOptions.
    /// Options are validated at startup only when file search is enabled, so a disabled feature never blocks startup.
    /// </summary>
    public static IServiceCollection AddFileSearchChatService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileSearchOptions>()
            .Bind(configuration.GetSection(FileSearchOptions.SectionName))
            .Validate(o => o.Validate() is null, "Invalid Foundry:FileSearch configuration. See the Foundry:FileSearch section.")
            .ValidateOnStart();
        services.AddSingleton<IFileSearchChatService, FileSearchFoundryChatService>();
        return services;
    }
}
