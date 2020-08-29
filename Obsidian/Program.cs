using Newtonsoft.Json;
using Obsidian.Logging;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian
{
    public static class Program
    {
        private static Dictionary<int,Server> Servers = new Dictionary<int, Server>();
        private static List<Task> Tasks = new List<Task>();

        public static GlobalConfig Config;
        public static Random Random = new Random();


        public static readonly AsyncLogger RegistryLogger = new AsyncLogger("Registry", LogLevel.Debug, "registry.log");
        public static readonly AsyncLogger PacketLogger = new AsyncLogger("Packets", LogLevel.Debug, Directory.GetCurrentDirectory());


        private static async Task Main(string[] args)
        {
            string version = "0.1";
#if DEBUG
            version += "-DEV";
#endif
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine(asciilogo);
            Console.ResetColor();

            if (!File.Exists("global_config.json"))
            {
                File.WriteAllText("global_config.json", JsonConvert.SerializeObject(new GlobalConfig(), Formatting.Indented));
                Console.WriteLine("Created new global config");
            }

            Config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("global_config.json"));

            for (int i = 0; i < Config.ServerCount; i++)
            {
                string serverDir = $"Server-{i}";

                Directory.CreateDirectory(serverDir);

                string configPath = Path.Combine(serverDir, "config.json");

                if (!File.Exists(configPath))
                {
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(new Config(), Formatting.Indented));
                    Console.WriteLine("Created new config");
                }

                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));

                Servers.Add(i, new Server(config, version, i));
            }

            foreach (var (key, server) in Servers)
            {
                Tasks.Add(Task.Run(async delegate ()
                {
                    await server.StartServer();
                }));
            }

            await Task.WhenAll(Tasks); //Wait until all servers are dead.

            // Do some entry stuff for server class
            Console.WriteLine("Server killed. Press any key to Return.");
            Console.ReadKey();
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