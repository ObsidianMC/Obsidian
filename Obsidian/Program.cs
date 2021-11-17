using System.Globalization;
using System.IO;
using System.Reflection;
using CommandLine = Obsidian.IO.Console.CommandLine;

namespace Obsidian;

public static class Program
{
    private static Server Server;
    private static readonly TaskCompletionSource<bool> cancelKeyPress = new();
    private static bool shutdownPending;

    private static async Task Main()
    {
#if RELEASE
            string version = "0.1";
#else
        string version = "0.1-DEV";
        string asmpath = Assembly.GetExecutingAssembly().Location;
        //This will strip just the working path name:
        //C:\Program Files\MyApplication
        string asmdir = Path.GetDirectoryName(asmpath);
        Environment.CurrentDirectory = asmdir;
#endif
        // Kept for consistant number parsing
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        CommandLine.TakeControl();
        CommandLine.Title = $"Obsidian {version}";
        CommandLine.BackgroundColor = ConsoleColor.White;
        CommandLine.ForegroundColor = ConsoleColor.Black;
        CommandLine.WriteLine(asciilogo);
        CommandLine.ResetColor();
        CommandLine.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.protocol.GetDescription()}");

        CommandLine.RegisterCommand("help", HelpCommand);
        CommandLine.RegisterCommand("eval", EvalCommand);
        CommandLine.RegisterCommand("stop", StopCommand);

        CommandLine.CancelKeyPress += OnConsoleCancelKeyPress;

        string serverDir = string.Empty; // This resolves to the binary path by default.

        string configPath = Path.Combine(serverDir, "config.json");
        Config config;

        var configFile = new FileInfo(configPath);

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

        Server = new Server(config, version, serverDir);
        await Server.StartServerAsync();

        if (!shutdownPending)
        {
            CommandLine.WriteLine("Server killed. Press any key to return...");
            CommandLine.WaitForExit();
        }
    }

    private static bool OnConsoleCancelKeyPress()
    {
        StopProgram();
        return true;
    }

    /// <summary>
    /// Gracefully shuts sub-servers down and exits Obsidian.
    /// </summary>
    private static void StopProgram()
    {
        shutdownPending = true;
        Server.StopServer();
    }

    private static ValueTask HelpCommand(string[] args, ReadOnlyMemory<char> fullArgs)
    {
        CommandLine.WriteLine(@"help - list all commands
eval /<command> - executes the command
stop - stops the current server");
        return ValueTask.CompletedTask;
    }

    private static async ValueTask EvalCommand(string[] args, ReadOnlyMemory<char> fullArgs)
    {
        await Server.ExecuteCommand(fullArgs);
    }

    private static ValueTask StopCommand(string[] args, ReadOnlyMemory<char> fullArgs)
    {
        Server.StopServer();
        return ValueTask.CompletedTask;
    }

    // Cool startup console logo because that's cool
    // 10/10 -IGN
    private const string asciilogo =
        "\n" +
        "      ▄▄▄▄   ▄▄       ▄▄▄▄  ▀   ▄▄▄   ▐ ▄ \n" +
        "      ▐█ ▀█ ▐█ ▀  ██ ██  ██ ██ ▐█ ▀█  █▌▐█\n" +
        " ▄█▀▄ ▐█▀▀█▄▄▀▀▀█▄▐█ ▐█  ▐█▌▐█ ▄█▀▀█ ▐█▐▐▌\n" +
        "▐█▌ ▐▌██▄ ▐█▐█▄ ▐█▐█▌██  ██ ▐█▌▐█  ▐▌██▐█▌\n" +
        " ▀█▄▀  ▀▀▀▀  ▀▀▀▀ ▀▀▀▀▀▀▀▀  ▀▀▀ ▀  ▀ ▀▀ █ \n\n";
}