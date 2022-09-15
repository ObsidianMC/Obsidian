using Obsidian.API;
using Obsidian.Hosting;
using Obsidian.Utilities;
using System.Threading;

namespace Obsidian.ConsoleApp;

public class ConsoleServerSetup : IServerSetup
{
    public async Task<IServerConfiguration> LoadServerConfiguration(CancellationToken cToken)
    {
        if (!Directory.Exists("config"))
        {
            Directory.CreateDirectory("config");
        }

        var configFile = new FileInfo(Path.Combine("config", "main.json"));

        if (configFile.Exists)
        {
            using var configFileStream = configFile.OpenRead();
            var c = await configFileStream.FromJsonAsync<ServerConfiguration>(cancellationToken: cToken);
            return c ?? throw new Exception("Server config file exists, but is invalid. Is it corrupt?");
        }

        var config = new ServerConfiguration();

        using var fs = configFile.Create();

        await config.ToJsonAsync(fs, cancellationToken: cToken);

        Console.WriteLine($"Created new configuration file for Server");
        Console.WriteLine($"Please fill in your config with the values you wish to use for your server.\n");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(configFile.FullName);
        Console.ResetColor();
        Console.ReadKey();
        Environment.Exit(0);
        throw new Exception("Unreachable?");
    }


    public async Task<List<ServerWorld>> LoadServerWorlds(CancellationToken cToken)
    {
        if (!Directory.Exists("config"))
        {
            Directory.CreateDirectory("config");
        }

        var worldsFile = new FileInfo(Path.Combine("config", "worlds.json"));

        if (worldsFile.Exists)
        {
            using var worldsFileStream = worldsFile.OpenRead();
            var w = await worldsFileStream.FromJsonAsync<List<ServerWorld>>(cancellationToken: cToken);
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

        await worlds.ToJsonAsync(fs, cancellationToken: cToken);

        return worlds;
    }
}

