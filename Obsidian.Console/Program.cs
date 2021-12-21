using Obsidian.Utilities;
using System.Globalization;
using Obsidian.ConsoleApp.IO;
using Obsidian.Logging;

namespace Obsidian.ConsoleApp;

public static class Program
{
    private static async Task Main()
    {
#if RELEASE
        string version = "0.1";
#else
        string version = "0.1-DEV";
        Environment.CurrentDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
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

        string configPath = Path.Combine(serverDirectory, "config.json");
        var configFile = new FileInfo(configPath);
        Config config;

        if (configFile.Exists)
        {
            using var fs = configFile.OpenRead();
            config = await fs.FromJsonAsync<Config>();
        }
        else
        {
            config = new Config();

            using var fs = configFile.Create();

            await config.ToJsonAsync(fs);

            CommandLine.WriteLine($"Created new configuration file for Server");
            CommandLine.WriteLine($"Please fill in your config with the values you wish to use for your server.\n");
            CommandLine.ForegroundColor = ConsoleColor.Green;
            CommandLine.WriteLine(configPath);
            CommandLine.ResetColor();
            return;
        }

        var loggerProvider = new LoggerProvider((category, logLevel) => new Logger(category, logLevel));
        var server = new Server(config, version, serverDirectory, loggerProvider);

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
