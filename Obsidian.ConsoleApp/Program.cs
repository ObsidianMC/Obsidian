using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.ConsoleApp.Logging;
using Obsidian.Hosting;
using Obsidian.Utilities;

namespace Obsidian.ConsoleApp;

public static partial class Program
{
    private static async Task Main()
    {
        Console.Title = $"Obsidian {Server.VERSION}";
        Console.BackgroundColor = ConsoleColor.White;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.CursorVisible = false;
        Console.WriteLine(asciilogo);
        Console.ResetColor();
        Console.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.DefaultProtocol.GetDescription()}");

        IServerEnvironment env = await DefaultServerEnvironment.Create();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddObsidian(env);
            })
            .ConfigureLogging(options =>
            {
                options.ClearProviders();
                options.AddProvider(new LoggerProvider());
                //  Shhh... Only let Microsoft log when stuff crashes.
                options.AddFilter("Microsoft", LogLevel.Warning);
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
