using Obsidian.Utilities;
using System.Globalization;
using Obsidian.ConsoleApp.IO;
using Obsidian.Logging;

namespace Obsidian.ConsoleApp;

public static class Program
{
    private static async Task Main(string[] args)
    {
#if RELEASE
        string version = "0.1";
#else
        string version = "0.1-DEV";
        Environment.CurrentDirectory = args.Length > 0
            ? string.Join(' ', args)
            : Path.GetDirectoryName(Environment.ProcessPath)!;
#endif
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        CommandLine.TakeControl();

        CommandLine.Title = $"Obsidian {version}";
        CommandLine.BackgroundColor = ConsoleColor.White;
        CommandLine.ForegroundColor = ConsoleColor.Black;
        CommandLine.WriteLine(asciiLogo);
        CommandLine.ResetColor();
        CommandLine.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.DefaultProtocol.GetDescription()}");

        string serverDirectory = string.Empty; // This resolves to the binary path by default.

        Config? config = await TryLoadConfigAsync();
        if (config is null)
            return;

        List<ServerWorld> worlds = await TryLoadServerWorldsAsync();
        var loggerProvider = new LoggerProvider((category, logLevel) => new Logger(category, logLevel));
        var server = new Server(config, version, serverDirectory, worlds, loggerProvider);

        CommandLine.RegisterCommand("exit", (_, _) => StopServerAsync(server));
        CommandLine.RegisterCommand("e", (_, args) => ExecuteAsync(args, server));
        CommandLine.RegisterCommand("execute", (_, args) => ExecuteAsync(args, server));

        await server.RunAsync();

        CommandLine.WriteLine("Server killed. Press any key to return...");
        CommandLine.WaitForExit();
    }

    private static ValueTask StopServerAsync(Server server)
    {
        server.Stop();
        return ValueTask.CompletedTask;
    }

    private static async ValueTask ExecuteAsync(ReadOnlyMemory<char> args, Server server)
    {
        await server.ExecuteCommand(args);
    }

    private static async Task<List<ServerWorld>> TryLoadServerWorldsAsync()
    {
        var worldsFile = new FileInfo("worlds.json");

        if (worldsFile.Exists)
        {
            using var worldsFileStream = worldsFile.OpenRead();

            return await worldsFileStream.FromJsonAsync<List<ServerWorld>>();
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

    private static async Task<Config?> TryLoadConfigAsync()
    {
        var configFile = new FileInfo("config.json");

        if (configFile.Exists)
        {
            using var configFileStream = configFile.OpenRead();

            return await configFileStream.FromJsonAsync<Config>();
        }

        var config = new Config();

        using var fs = configFile.Create();

        await config.ToJsonAsync(fs);

        Console.WriteLine($"Created new configuration file for Server");
        Console.WriteLine($"Please fill in your config with the values you wish to use for your server.\n");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(configFile.FullName);
        Console.ResetColor();

        return null;
    }

    // Cool startup console logo because that's cool
    // 10/10 -IGN
    private const string asciiLogo =
        "\n" +
        "      ▄▄▄▄   ▄▄       ▄▄▄▄  ▀   ▄▄▄   ▐ ▄ \n" +
        "      ▐█ ▀█ ▐█ ▀  ██ ██  ██ ██ ▐█ ▀█  █▌▐█\n" +
        " ▄█▀▄ ▐█▀▀█▄▄▀▀▀█▄▐█ ▐█  ▐█▌▐█ ▄█▀▀█ ▐█▐▐▌\n" +
        "▐█▌ ▐▌██▄ ▐█▐█▄ ▐█▐█▌██  ██ ▐█▌▐█  ▐▌██▐█▌\n" +
        " ▀█▄▀  ▀▀▀▀  ▀▀▀▀ ▀▀▀▀▀▀▀▀  ▀▀▀ ▀  ▀ ▀▀ █ \n\n";
}
