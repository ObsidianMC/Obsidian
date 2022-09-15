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

    public static IServiceCollection AddObsidian<T>(this IServiceCollection services) where T : class, IServerSetup, new()
    {
        var setup = Activator.CreateInstance<T>();
        return AddObsidian(services, setup);
    }

    public static IServiceCollection AddObsidian(this IServiceCollection services)
    {
        return AddObsidian<DefaultServerStartup>(services);
    }
}
