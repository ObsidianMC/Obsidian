using Newtonsoft.Json;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian
{
    public static class Program
    {
        private static Dictionary<int, Server> Servers = new Dictionary<int, Server>();
        private static TaskCompletionSource<bool> cancelKeyPress = new TaskCompletionSource<bool>();

        private const string globalConfigFile = "global_config.json";

        private static async Task Main()
        {
#if RELEASE
            string version = "0.1";
#else
            string version = "0.1-DEV";
#endif
            // Kept for consistant number parsing
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Console.Title = $"Obsidian {version}";
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine(asciilogo);
            Console.ResetColor();

            Console.CancelKeyPress += OnConsoleCancelKeyPressed;

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

            Console.WriteLine("Server(s) killed. Press any key to return...");
            Console.ReadKey(intercept: false);
        }

        private static void OnConsoleCancelKeyPressed(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            foreach (var (_, server) in Servers)
                server.StopServer();

            cancelKeyPress.SetResult(true);
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