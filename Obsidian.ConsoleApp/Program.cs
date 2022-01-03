using Obsidian.Utilities;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Obsidian.ConsoleApp;

public static class Program
{
    private static Server Server;
    private static readonly TaskCompletionSource<bool> cancelKeyPress = new();
    private static bool shutdownPending;

    /// <summary>
    /// Event handler for Windows console events
    /// </summary>
    private static NativeMethods.HandlerRoutine _windowsConsoleEventHandler;
    private const string globalConfigFile = "global_config.json";

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

        Console.Title = $"Obsidian {version}";
        Console.BackgroundColor = ConsoleColor.White;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.CursorVisible = false;
        Console.WriteLine(asciilogo);
        Console.ResetColor();
        Console.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.DefaultProtocol.GetDescription()}");

        // Hook into Windows' native console closing events, otherwise use .NET's native event.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _windowsConsoleEventHandler += new NativeMethods.HandlerRoutine(OnConsoleEvent);
            NativeMethods.SetConsoleCtrlHandler(_windowsConsoleEventHandler, true);
        }
        else
        {
            Console.CancelKeyPress += OnConsoleCancelKeyPressed;
        }

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

            Console.WriteLine($"Created new configuration file for Server");
            Console.WriteLine($"Please fill in your config with the values you wish to use for your server.\n");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(configPath);
            Console.ResetColor();
            return;
        }

        InitConsoleInput();

        Server = new Server(config, version, serverDir);
        await Server.RunAsync();

        if (!shutdownPending)
        {
            Console.WriteLine("Server killed. Press any key to return...");
            Console.ReadKey(intercept: false);
        }
    }

    private static void InitConsoleInput()
    {
        Task.Run(async () =>
        {
            Server currentServer = Program.Server;
            await Task.Delay(2000);
            while (!shutdownPending)
            {
                if (currentServer == null)
                    break;

                string input = ConsoleIO.ReadLine();
                await currentServer.ExecuteCommand(input);
            }
        });
    }

    private static void OnConsoleCancelKeyPressed(object sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        StopProgram();
        cancelKeyPress.SetResult(true);
    }

    private static bool OnConsoleEvent(NativeMethods.CtrlType ctrlType)
    {
        Console.WriteLine("Received {0}", ctrlType);
        StopProgram();
        return true;
    }

    /// <summary>
    /// Gracefully shuts sub-servers down and exits Obsidian.
    /// </summary>
    private static void StopProgram()
    {
        shutdownPending = true;
        Server.Stop();
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
