using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Obsidian.API.Configuration;
using Obsidian.Commands.Framework;
using Obsidian.Net.Rcon;
using Obsidian.Services;
using Obsidian.WorldData;
using System.IO;

namespace Obsidian.Hosting;
public static class DependencyInjection
{
    public static IHostApplicationBuilder ConfigureObsidian(this IHostApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile(Path.Combine("config", "server.json"), optional: false, reloadOnChange: true);
        builder.Configuration.AddJsonFile(Path.Combine("config", "whitelist.json"), optional: false, reloadOnChange: true);
        builder.Configuration.AddEnvironmentVariables();

        return builder;
    }

    public static IHostApplicationBuilder AddObsidian(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<ServerConfiguration>(builder.Configuration);
        builder.Services.Configure<WhitelistConfiguration>(builder.Configuration);

        builder.Services.AddSingleton<IServerEnvironment, DefaultServerEnvironment>();
        builder.Services.AddSingleton<CommandHandler>();
        builder.Services.AddSingleton<RconServer>();
        builder.Services.AddSingleton<WorldManager>();
        builder.Services.AddSingleton<PacketBroadcaster>();
        builder.Services.AddSingleton<IServer, Server>();
        builder.Services.AddSingleton<IUserCache, UserCache>();
        builder.Services.AddSingleton<EventDispatcher>();

        builder.Services.AddHttpClient();

        builder.Services.AddHostedService(sp => sp.GetRequiredService<PacketBroadcaster>());
        builder.Services.AddHostedService<ObsidianHostingService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WorldManager>());

        builder.Services.AddSingleton<IWorldManager>(sp => sp.GetRequiredService<WorldManager>());
        builder.Services.AddSingleton<IPacketBroadcaster>(sp => sp.GetRequiredService<PacketBroadcaster>());
        
        return builder;
    }

}
