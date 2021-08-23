using Microsoft.CodeAnalysis;
using Obsidian.Utilities;
using System;
using System.Collections.Generic;
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
        private static readonly Dictionary<int, Server> Servers = new();
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
            Console.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.protocol.GetDescription()}");

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

            var globalConfigFile = new FileInfo(Program.globalConfigFile);

            if (globalConfigFile.Exists)
            {
                using var fs = globalConfigFile.OpenRead();
               
                Globals.Config = await fs.FromJsonAsync<GlobalConfig>();
            }
            else
            {
                Globals.Config = new GlobalConfig();

                using var fs = globalConfigFile.Create();

                await Globals.Config.ToJsonAsync(fs);

                Console.WriteLine("Created new global configuration file");
            }

            for (int i = 0; i < Globals.Config.ServerCount; i++)
            {
                string serverDir = $"Server-{i}";

                Directory.CreateDirectory(serverDir);

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

                    Console.WriteLine($"Created new configuration file for Server-{i}");
                }

                Servers.Add(i, new Server(config, version, i));
            }

            if (Servers.GroupBy(entry => entry.Value.Port).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Multiple servers cannot be bound to the same port");

            var serverTasks = Servers.Select(async entry => await entry.Value.StartServerAsync());
            InitConsoleInput();
            await Task.WhenAny(cancelKeyPress.Task, Task.WhenAll(serverTasks));

            if (!shutdownPending)
            {
                Console.WriteLine("Server(s) killed. Press any key to return...");
                Console.ReadKey(intercept: false);
            }
        }

        private static void InitConsoleInput()
        {
            Task.Run(async () =>
            {
                Server currentServer = Servers.First().Value;
                await Task.Delay(2000);
                while (!shutdownPending)
                {
                    if (currentServer == null && Servers.Count == 0)
                        break;

                    string input = ConsoleIO.ReadLine();

                    if (input.StartsWith('.'))
                    {
                        if (input.StartsWith(".switch"))
                        {
                            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length < 2)
                            {
                                ConsoleIO.WriteLine("Invalid server id");
                                continue;
                            }
                            if (!int.TryParse(parts[1], out int serverId))
                            {
                                ConsoleIO.WriteLine("Invalid server id");
                                continue;
                            }
                            if (!Servers.TryGetValue(serverId, out var server))
                            {
                                ConsoleIO.WriteLine("No server with given id found");
                                continue;
                            }

                            currentServer = server;
                            ConsoleIO.WriteLine($"Changed current server to {server.Id}");
                        }
                        else if (input.StartsWith(".execute"))
                        {
                            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length < 3)
                            {
                                ConsoleIO.WriteLine("Invalid server id or command");
                                continue;
                            }
                            if (!int.TryParse(parts[1], out int serverId))
                            {
                                ConsoleIO.WriteLine("Invalid server id");
                                continue;
                            }
                            if (!Servers.TryGetValue(serverId, out var server))
                            {
                                ConsoleIO.WriteLine("No server with given id found");
                                continue;
                            }

                            ConsoleIO.WriteLine($"Executing command on Server-{server.Id}");
                            await server.ExecuteCommand(string.Join(' ', parts.Skip(2)));
                        }
                    }
                    else
                    {
                        await currentServer.ExecuteCommand(input);
                        if (input == "stop")
                        {
                            Servers.Remove(currentServer.Id);
                            currentServer = Servers.FirstOrDefault().Value;
                        }
                    }
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

            foreach (var (_, server) in Servers)
                server.StopServer();
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
}
