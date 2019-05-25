using Newtonsoft.Json;
using Obsidian.Entities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian
{
    class Program
    {
        static Server Obsidian;

        static async Task Main(string[] args)
        {
            string version = "0.1";
            #if DEBUG
                version += "-DEVEL";
#endif
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine(asciilogo);
            Console.ResetColor();

            // Do some entry stuff for server class
            if (!File.Exists("config.json"))
            {
                File.Create("config.json").Close();
                File.WriteAllText("config.json", JsonConvert.SerializeObject(new Config()));
                Console.WriteLine("(Created new config)");
            }

            var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
            Obsidian = new Server(config, version, "0");

            await Obsidian.StartServer();

            Console.WriteLine("Server killed. Press any key to Return.");
            Console.ReadKey();
        }

        // Cool startup console logo because that's cool
        // 10/10 -IGN
        const string asciilogo =
            "\n"+
            "      ▄▄▄▄   ▄▄       ▄▄▄▄  ▀   ▄▄▄   ▐ ▄ \n" +
            "      ▐█ ▀█ ▐█ ▀  ██ ██  ██ ██ ▐█ ▀█  █▌▐█\n"+
            " ▄█▀▄ ▐█▀▀█▄▄▀▀▀█▄▐█ ▐█  ▐█▌▐█ ▄█▀▀█ ▐█▐▐▌\n"+
            "▐█▌ ▐▌██▄ ▐█▐█▄ ▐█▐█▌██  ██ ▐█▌▐█  ▐▌██▐█▌\n"+
            " ▀█▄▀  ▀▀▀▀  ▀▀▀▀ ▀▀▀▀▀▀▀▀  ▀▀▀ ▀  ▀ ▀▀ █ \n\n";
    }
}
