using Microsoft.Extensions.DependencyInjection;
using Obsidian.Commands.Framework;
using Obsidian.Net.Rcon;
using Obsidian.WorldData;
using System.ComponentModel.Design;

namespace Obsidian.Hosting;
public static class DependencyInjection
{
    public static IServiceCollection AddObsidian(this IServiceCollection services, IServerEnvironment env)
    {
        services.AddSingleton(env);
        services.AddSingleton(env.Configuration);

        services.AddSingleton<WorldManager>();
        services.AddSingleton<CommandHandler>();
        services.AddSingleton<RconServer>();
        services.AddSingleton<Server>();
        services.AddSingleton<IServer>(f => f.GetRequiredService<Server>());
        services.AddHostedService<ObsidianHostingService>();
        return services;
    }
}
