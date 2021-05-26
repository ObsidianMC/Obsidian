using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using Obsidian.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CL = Obsidian.IO.Console.CommandLine;

namespace Obsidian
{
    public static class Program
    {
        private static readonly Dictionary<int, Server> Servers = new();
        private static Server activeServer;
        private static bool shutdownPending;

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

            CL.TakeControl();
            CL.Title = $"Obsidian {version}";
            CL.BackgroundColor = ConsoleColor.White;
            CL.ForegroundColor = ConsoleColor.Black;
            CL.WriteLine(asciilogo);
            CL.ResetColor();
            CL.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.protocol.GetDescription()}");

            CL.CancelKeyPress += OnConsoleCancelKeyPress;

            CL.RegisterCommand("switch", SwitchCommand);
            CL.RegisterCommand("eval", EvalCommand);
            CL.RegisterCommand("stop", StopCommand);
            CL.RegisterCommand("stopall", ExitCommand);
            CL.RegisterCommand("help", HelpCommand);

            if (File.Exists(globalConfigFile))
            {
                Globals.Config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText(globalConfigFile));
            }
            else
            {
                Globals.Config = new GlobalConfig();
                File.WriteAllText(globalConfigFile, JsonConvert.SerializeObject(Globals.Config, Formatting.Indented));
                CL.WriteLine("Created new global configuration file");
            }

            if (Globals.Config.ServerCount < 1)
            {
                CL.WriteLine($"Server count is set to {Globals.Config.ServerCount}, no servers were run.");
                CL.WaitForExit();
                return;
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
                    CL.WriteLine($"Created new configuration file for Server-{i}");
                }

                Servers.Add(i, new Server(config, version, i));
            }

            if (Servers.GroupBy(entry => entry.Value.Port).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Multiple servers cannot be binded to the same port");

            TrySetActiveServer(index: 0);

            var serverTasks = Servers.Select(entry => entry.Value.StartServerAsync());
            await Task.WhenAll(serverTasks);

            CL.WriteLine("Server(s) killed. Press any key to return...");
            CL.WaitForExit();
        }

        private static ValueTask HelpCommand(string[] args, ReadOnlyMemory<char> fullArgs)
        {
            CL.WriteLine(@"help - list all commands
switch - set commands scope to global
switch <id> - set commands scope to a specific server
eval /<command> - executes the command
stop - stops the current server
stopall - stops all servers");
            return ValueTask.CompletedTask;
        }

        private static ValueTask SwitchCommand(string[] args, ReadOnlyMemory<char> fullArgs)
        {
            if (args.Length == 0)
            {
                activeServer = null;
                CL.ResetCommandPrefix();
                Success("Switched successfully");
                return ValueTask.CompletedTask;
            }

            if (args.Length != 1)
            {
                Error($"Switch command accepts 0 or 1 argument");
                return ValueTask.CompletedTask;
            }

            if (int.TryParse(args[0], out int index))
            {
                if (TrySetActiveServer(index))
                {
                    Success($"Switched to server {index}");
                }
                else
                {

                    Error($"No server with ID {index} found");
                }
            }
            else
            {
                Error("Invalid server ID");
            }

            return ValueTask.CompletedTask;
        }

        private static async ValueTask EvalCommand(string[] args, ReadOnlyMemory<char> fullArgs)
        {
            await activeServer?.ExecuteCommand(fullArgs);
        }

        private static ValueTask StopCommand(string[] args, ReadOnlyMemory<char> fullArgs)
        {
            if (activeServer is null)
            {
                Error("No server selected");
                return ValueTask.CompletedTask;
            }

            CL.ResetCommandPrefix();
            Servers.Remove(activeServer.Id);
            activeServer.StopServer();
            activeServer = null;

            Success("Server stopped");
            return ValueTask.CompletedTask;
        }

        private static ValueTask ExitCommand(string[] args, ReadOnlyMemory<char> fullArgs)
        {
            activeServer = null;
            CL.ResetCommandPrefix();
            StopProgram();
            return ValueTask.CompletedTask;
        }

        private static bool TrySetActiveServer(int index)
        {
            if (Servers.TryGetValue(index, out var server))
            {
                if (server == activeServer)
                    return true;

                activeServer = server;
                CL.CommandPrefix = $"Server-{index}> ";
                return true;
            }
            return false;
        }

        private static void Error(string message)
        {
            CL.ForegroundColor = ConsoleColor.Red;
            CL.BackgroundColor = ConsoleColor.Black;
            CL.WriteLine(message);
            CL.ResetColor();
        }

        private static void Success(string message)
        {
            CL.ForegroundColor = ConsoleColor.Green;
            CL.BackgroundColor = ConsoleColor.Black;
            CL.WriteLine(message);
            CL.ResetColor();
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