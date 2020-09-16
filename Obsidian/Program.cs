using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Obsidian.Chat;
using Obsidian.Util;
using Obsidian.Util.Converters;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Registry.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian
{
    public static class Program
    {
        private static Dictionary<int, Server> Servers = new Dictionary<int, Server>();
        private static List<Task> Tasks = new List<Task>();

        public static GlobalConfig Config { get; private set; }
        public static Random Random = new Random();
        
        internal static ILogger PacketLogger { get; set; }

        private static DefaultContractResolver ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        };

        public static JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = ContractResolver,
            Converters = new List<JsonConverter>
            {
                new DefaultEnumConverter<CustomDirection>(),
                new DefaultEnumConverter<Axis>(),
                new DefaultEnumConverter<Face>(),
                new DefaultEnumConverter<BlockFace>(),
                new DefaultEnumConverter<Half>(),
                new DefaultEnumConverter<Hinge>(),
                new DefaultEnumConverter<Instruments>(),
                new DefaultEnumConverter<Part>(),
                new DefaultEnumConverter<Shape>(),
                new DefaultEnumConverter<CustomDirection>(),
                new DefaultEnumConverter<MinecraftType>(),
                new DefaultEnumConverter<Attachment>(),
                new DefaultEnumConverter<ETextAction>(),
            }
        };
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
                if (Servers.Count(x => x.Value.Port == server.Port) > 1)
                    throw new InvalidOperationException("Servers cannot be binded to the same ports");

                Tasks.Add(Task.Run(async delegate ()
                {
                    await server.StartServer();
                }));
            }

            await Task.WhenAll(Tasks);//Wait until all servers are dead.

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