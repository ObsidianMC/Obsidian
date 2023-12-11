using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian.Hosting;

/// <summary>
/// A default <see cref="IServerEnvironment"/> implementation aimed for Console applications.
/// Loads the server configuration and worlds using the current working directory and
/// forwards commands from the standard input to the server.
/// 
/// Use the <see cref="CreateAsync"/> method to create an instance.
/// </summary>
public sealed class DefaultServerEnvironment : IServerEnvironment
{
    public bool ServerShutdownStopsProgram { get; } = true;
    public ServerConfiguration Configuration { get; }
    public List<ServerWorld> ServerWorlds { get; }

    private DefaultServerEnvironment(bool serverShutdownStopsProgram, ServerConfiguration configuration, List<ServerWorld> serverWorlds)
    {
        ServerShutdownStopsProgram = serverShutdownStopsProgram;
        Configuration = configuration;
        ServerWorlds = serverWorlds;
    }

    /// <summary>
    /// Provide server commands using the Console.
    /// </summary>
    /// <param name="server"></param>
    /// <param name="cToken"></param>
    /// <returns></returns>
    public async Task ProvideServerCommandsAsync(Server server, CancellationToken cToken)
    {
        while (!cToken.IsCancellationRequested)
        {
            var input = Console.ReadLine();
            if (input == null) continue;
            await server.ExecuteCommand(input);
        }
    }

    Task IServerEnvironment.OnServerStoppedGracefullyAsync(ILogger logger)
    {
        logger.LogInformation("Goodbye!");
        return Task.CompletedTask;
    }
    Task IServerEnvironment.OnServerCrashAsync(ILogger logger, Exception e)
    {
        // Write crash log somewhere?
        var byeMessages = new[]
        {
            "We had a good run...",
            "At least we tried...",
            "Who could've seen this one coming...",
            "Try turning it off and on again...",
            "I blame Naamloos for this one...",
            "I blame Sebastian for this one...",
            "I blame Tides for this one...",
            "I blame Craftplacer for this one..."
        };

        logger.LogCritical("Obsidian has crashed!");
        logger.LogCritical("{message}", byeMessages[new Random().Next(byeMessages.Length)]);
        logger.LogCritical(e, "Reason: {reason}", e.Message);
        logger.LogCritical("{}", e.StackTrace);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Create a <see cref="DefaultServerEnvironment"/> asynchronously.
    /// </summary>
    /// <returns></returns>
    public static async Task<DefaultServerEnvironment> CreateAsync()
    {
        var config = await LoadServerConfigurationAsync();
        var worlds = await LoadServerWorldsAsync();
        return new DefaultServerEnvironment(true, config, worlds);
    }
    private static async Task<ServerConfiguration> LoadServerConfigurationAsync()
    {
        if (!Directory.Exists("config"))
            Directory.CreateDirectory("config");

        var configFile = new FileInfo(Path.Combine("config", "main.json"));

        if (configFile.Exists)
        {
            await using var configFileStream = configFile.OpenRead();
            return await configFileStream.FromJsonAsync<ServerConfiguration>()
                ?? throw new Exception("Server config file exists, but is invalid. Is it corrupt?");
        }

        var config = new ServerConfiguration();

        await using var fileStream = configFile.Create();

        await config.ToJsonAsync(fileStream);
        await fileStream.FlushAsync();

        Console.WriteLine($"Created new configuration file for Server");
        Console.WriteLine($"Please fill in your config with the values you wish to use for your server.");
        Console.WriteLine(configFile.FullName);

        Console.ReadKey();
        Environment.Exit(0);

        throw new UnreachableException();
    }
    private static async Task<List<ServerWorld>> LoadServerWorldsAsync()
    {
        if (!Directory.Exists("config"))
            Directory.CreateDirectory("config");

        var worldsFile = new FileInfo(Path.Combine("config", "worlds.json"));

        if (worldsFile.Exists)
        {
            await using var worldsFileStream = worldsFile.OpenRead();
            return await worldsFileStream.FromJsonAsync<List<ServerWorld>>()
                ?? throw new Exception("A worlds file does exist, but is invalid. Is it corrupt?");
        }

        var worlds = new List<ServerWorld>()
            {
                new()
                {
                    ChildDimensions =
                    {
                        "minecraft:the_nether",
                        "minecraft:the_end"
                    }
                }
            };

        await using var fileStream = worldsFile.Create();
        await worlds.ToJsonAsync(fileStream);

        return worlds;
    }


}

