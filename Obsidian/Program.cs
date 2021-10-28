using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Obsidian.Logging;
using Obsidian.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Obsidian
{
    public static class Program
    {
        private static Server server;
        private static readonly TaskCompletionSource<bool> CancelKeyPress = new();
        private static bool shutdownPending;

        /// <summary>
        /// Event handler for Windows console events
        /// </summary>
        private static NativeMethods.HandlerRoutine windowsConsoleEventHandler;

        private static async Task Main()
        {
#if RELEASE
            string version = "0.1";
#else
            string version = "0.1-DEV";
            var asmDir = AppContext.BaseDirectory;
            Environment.CurrentDirectory = asmDir;
#endif
            // Kept for consistant number parsing
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Console.Title = $"Obsidian {version}";
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.CursorVisible = false;
            Console.WriteLine(AsciiLogo);
            Console.ResetColor();
            Console.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.protocol.GetDescription()}");

            // Hook into Windows' native console closing events, otherwise use .NET's native event.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                windowsConsoleEventHandler += new NativeMethods.HandlerRoutine(OnConsoleEvent);
                NativeMethods.SetConsoleCtrlHandler(windowsConsoleEventHandler, true);
            }
            else
            {
                Console.CancelKeyPress += OnConsoleCancelKeyPressed;
            }

            const string configPath = "config.json";
            var configFile = new FileInfo(configPath);
            Config config = new Config();

            if (configFile.Exists)
            {
                await using var fs = configFile.OpenRead();
                config = await fs.FromJsonAsync<Config>();
            }
            else
            {
                await using var fs = configFile.Create();
                await config.ToJsonAsync(fs);

                Console.WriteLine("Created new server config file");
            }

#if DEBUG
            config!.LogLevel = LogLevel.Debug;
#endif
            
            Globals.Config = config;

            server = new Server(version);
            
            var serverTask = server.StartServerAsync();
            InitConsoleInput();
            await Task.WhenAny(CancelKeyPress.Task, serverTask);

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
                await Task.Delay(2000);
                while (!shutdownPending)
                {
                    if (server is null)
                        break;

                    string input = ConsoleIO.ReadLine();

                    await server.ExecuteCommand(input);
                }
            });
        }

        private static void OnConsoleCancelKeyPressed(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            StopProgram();
            CancelKeyPress.SetResult(true);
        }

        private static bool OnConsoleEvent(NativeMethods.CtrlType ctrlType)
        {
            Console.WriteLine("Received {0}", ctrlType);
            StopProgram();
            return true;
        }

        /// <summary>
        /// Gracefully shuts server down and exits Obsidian.
        /// </summary>
        private static void StopProgram()
        {
            shutdownPending = true;
            
            server.StopServer();
        }

        // Cool startup console logo because that's cool
        // 10/10 -IGN
        private const string AsciiLogo =
            "\n" +
            "      ▄▄▄▄   ▄▄       ▄▄▄▄  ▀   ▄▄▄   ▐ ▄ \n" +
            "      ▐█ ▀█ ▐█ ▀  ██ ██  ██ ██ ▐█ ▀█  █▌▐█\n" +
            " ▄█▀▄ ▐█▀▀█▄▄▀▀▀█▄▐█ ▐█  ▐█▌▐█ ▄█▀▀█ ▐█▐▐▌\n" +
            "▐█▌ ▐▌██▄ ▐█▐█▄ ▐█▐█▌██  ██ ▐█▌▐█  ▐▌██▐█▌\n" +
            " ▀█▄▀  ▀▀▀▀  ▀▀▀▀ ▀▀▀▀▀▀▀▀  ▀▀▀ ▀  ▀ ▀▀ █ \n\n";
    }
}
