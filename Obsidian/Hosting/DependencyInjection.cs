using Microsoft.Extensions.DependencyInjection;

namespace Obsidian.Hosting;
public static class DependencyInjection
{
    public static IServiceCollection AddObsidian(this IServiceCollection services, IServerSetup setup)
    {
        services.AddSingleton(setup);
        services.AddHostedService<ObsidianHostingService>();
        return services;
    }
}
