using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.Hosting;
using Obsidian.Utilities;

namespace Obsidian.ConsoleApp;

public static partial class Program
{
    private static async Task Main(params string[] args)
    {
        Console.Title = "Obsidian";
        Console.BackgroundColor = ConsoleColor.White;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.CursorVisible = false;
        Console.WriteLine(asciilogo);
        Console.ResetColor();
        Console.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.DefaultProtocol.GetDescription()}");

        var setup = new ConsoleServerSetup();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddObsidian(setup);
            })
            .ConfigureLogging(options =>
            {
                options.AddFilter("Microsoft", LogLevel.Critical);
            })
            .Build();

        await host.RunAsync();
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
