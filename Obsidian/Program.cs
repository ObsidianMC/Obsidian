using Newtonsoft.Json;
using Obsidian.Entities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian
{
    public static class Program
    {
        private static Server[] Servers;
        private static Task[] Tasks;
        public static GlobalConfig Config;

#if PACKETLOG
        public static Logger PacketLogger = new Logger("Packet Logger", LogLevel.Debug);
#endif

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

            if (!File.Exists("config.json"))
            {
                File.WriteAllText("config.json", JsonConvert.SerializeObject(new GlobalConfig(), Formatting.Indented));
                Console.WriteLine("Created new global config");
            }

            Config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("config.json"));

            Tasks = new Task[Config.ServerCount];
            Servers = new Server[Config.ServerCount];
            for (int i = 0; i < Servers.Length; i++)
            {
                if (!Directory.Exists(i.ToString()))
                {
                    Directory.CreateDirectory(i.ToString());
                }

                string configPath = Path.Combine(i.ToString(), "config.json");

                if (!File.Exists(configPath))
                {
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(new Config(), Formatting.Indented));
                    Console.WriteLine("Created new config");
                }

                Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));

                Servers[i] = new Server(config, version, i);

                int capturedIndex = i; //HACK: race condition of the index
                Tasks[i] = Task.Run(async delegate ()
                {
                    await Servers[capturedIndex].StartServer();
                });
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