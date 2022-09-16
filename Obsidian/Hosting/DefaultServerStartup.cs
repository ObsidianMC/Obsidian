using System.IO;
using System.Threading;

namespace Obsidian.Hosting;

public sealed class DefaultServerEnvironment : IServerEnvironment
{
    public bool ServerShutdownStopsProgram { get; } = true;
    public IServerConfiguration Configuration { get; }
    public List<ServerWorld> ServerWorlds { get; }

    private DefaultServerEnvironment(bool serverShutdownStopsProgram, IServerConfiguration configuration, List<ServerWorld> serverWorlds)
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
    public async Task ProvideServerCommands(Server server, CancellationToken cToken)
    {
        while (!cToken.IsCancellationRequested)
        {
            var input = Console.ReadLine();
            if (input == null) continue;
            await server.ExecuteCommand(input);
        }
    }

    public static async Task<DefaultServerEnvironment> Create()
    {
        var config = await LoadServerConfiguration();
        var worlds = await LoadServerWorlds();
        return new DefaultServerEnvironment(true, config, worlds);
    }

    private static async Task<IServerConfiguration> LoadServerConfiguration()
    {
        if (!Directory.Exists("config"))
            Directory.CreateDirectory("config");

        var configFile = new FileInfo(Path.Combine("config", "main.json"));

        if (configFile.Exists)
        {
            using var configFileStream = configFile.OpenRead();
            var c = await configFileStream.FromJsonAsync<ServerConfiguration>();
            return c ?? throw new Exception("Server config file exists, but is invalid. Is it corrupt?");
        }

        var config = new ServerConfiguration();

        using var fs = configFile.Create();

        await config.ToJsonAsync(fs);

        Console.WriteLine($"Created new configuration file for Server");
        Console.WriteLine($"Please fill in your config with the values you wish to use for your server.\n");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(configFile.FullName);
        Console.ResetColor();
        Console.ReadKey();
        Environment.Exit(0);
        throw new Exception("Unreachable?");
    }
    private static async Task<List<ServerWorld>> LoadServerWorlds()
    {
        if (!Directory.Exists("config"))
            Directory.CreateDirectory("config");

        var worldsFile = new FileInfo(Path.Combine("config", "worlds.json"));

        if (worldsFile.Exists)
        {
            using var worldsFileStream = worldsFile.OpenRead();
            var w = await worldsFileStream.FromJsonAsync<List<ServerWorld>>();
            return w ?? throw new Exception("A worlds file does exist, but is invalid. Is it corrupt?");
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

        using var fs = worldsFile.Create();

        await worlds.ToJsonAsync(fs);

        return worlds;
    }

}

