using Microsoft.Extensions.DependencyInjection;
using Obsidian.Commands.Framework;
using Obsidian.Net.Rcon;
using Obsidian.Services;
using Obsidian.WorldData;

namespace Obsidian.Hosting;
public static class DependencyInjection
{
    public static IServiceCollection AddObsidian(this IServiceCollection services, IServerEnvironment env)
    {
        services.AddSingleton(env);
        services.AddSingleton(env.Configuration);
        services.AddSingleton<IServerConfiguration>(f => f.GetRequiredService<ServerConfiguration>());

        services.AddSingleton<CommandHandler>();
        services.AddSingleton<RconServer>();
        services.AddSingleton<WorldManager>();
        services.AddSingleton<PacketBroadcaster>();
        services.AddSingleton<IServer, Server>();

        services.AddHostedService(sp => sp.GetRequiredService<WorldManager>());
        services.AddHostedService(sp => sp.GetRequiredService<PacketBroadcaster>());
        services.AddHostedService<ObsidianHostingService>();

        services.AddSingleton<IWorldManager>(sp => sp.GetRequiredService<WorldManager>());
        services.AddSingleton<IPacketBroadcaster>(sp => sp.GetRequiredService<PacketBroadcaster>());

        return services;
    }

}
