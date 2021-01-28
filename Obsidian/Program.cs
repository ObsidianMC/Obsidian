﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Newtonsoft.Json;

using Obsidian.Util;
using Obsidian.Util.Extensions;

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

            if (File.Exists(globalConfigFile))
            {
                Globals.Config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText(globalConfigFile));
            }
            else
            {
                Globals.Config = new GlobalConfig();
                File.WriteAllText(globalConfigFile, JsonConvert.SerializeObject(Globals.Config, Formatting.Indented));
                Console.WriteLine("Created new global configuration file");
            }

            for (int i = 0; i < Globals.Config.ServerCount; i++)
            {
                string serverDir = $"Server-{i}";

                Directory.CreateDirectory(serverDir);

                string configPath = Path.Combine(serverDir, "config.json");
                Config config;

                if (File.Exists(configPath))
                {
                    config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                }
                else
                {
                    config = new Config();
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                    Console.WriteLine($"Created new configuration file for Server-{i}");
                }

                Servers.Add(i, new Server(config, version, i));
            }

            if (Servers.GroupBy(entry => entry.Value.Port).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Multiple servers cannot be binded to the same port");

            var serverTasks = Servers.Select(entry => entry.Value.StartServerAsync());

            await Task.WhenAny(cancelKeyPress.Task, Task.WhenAll(serverTasks));

            if (!shutdownPending)
            {
                Console.WriteLine("Server(s) killed. Press any key to return...");
                Console.ReadKey(intercept: false);
            }
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