using Microsoft.Extensions.DependencyInjection;
using Obsidian.Commands.Framework;
using Obsidian.Net.Rcon;
using Obsidian.WorldData;

namespace Obsidian.Hosting;
public static class DependencyInjection
{
    public static IServiceCollection AddObsidian(this IServiceCollection services, IServerEnvironment env)
    {
        services.AddSingleton(env);
        services.AddSingleton(env.Configuration);
        services.AddSingleton<IServerConfiguration>(f => f.GetRequiredService<ServerConfiguration>());

        services.AddSingleton<WorldManager>();
        services.AddSingleton<CommandHandler>();
        services.AddSingleton<Server>();
        services.AddSingleton<RconServer>();

        services.AddSingleton<IServer>(f => f.GetRequiredService<Server>());

        services.AddHostedService<ObsidianHostingService>();
        services.AddHostedService(x => x.GetRequiredService<WorldManager>());

        return services;
    }

}
