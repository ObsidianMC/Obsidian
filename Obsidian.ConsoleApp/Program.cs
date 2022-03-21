﻿using Obsidian.Utilities;
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

    private static async Task Main(params string[] args)
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
        if (args.Length > 0)
            Environment.CurrentDirectory = string.Join(' ', args);
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

        var config = await TryLoadConfigAsync();
        if (config == null)
            return;

        var serverWorlds = await TryLoadServerWorldsAsync();

        InitConsoleInput();

        Server = new Server(config, version, string.Empty, serverWorlds);
        await Server.RunAsync();

        if (!shutdownPending)
        {
            Console.WriteLine("Server killed. Press any key to return...");
            Console.ReadKey(intercept: false);
        }
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

    private static void InitConsoleInput()
    {
        Task.Run(async () =>
        {
            await Task.Delay(2000);
            while (!shutdownPending)
            {
                string? input = ConsoleIO.ReadLine();
                if (!string.IsNullOrEmpty(input))
                    await Server.ExecuteCommand(input);
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
