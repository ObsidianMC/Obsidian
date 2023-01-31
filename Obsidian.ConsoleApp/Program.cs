using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.ConsoleApp.Logging;
using Obsidian.Hosting;
using Obsidian.Utilities;

namespace Obsidian.ConsoleApp;

public static class Program
{
    private static async Task Main()
    {
        Console.Title = $"Obsidian for {Server.DefaultProtocol} ({Server.VERSION})";
        Console.BackgroundColor = ConsoleColor.White;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.CursorVisible = false;
        Console.WriteLine(asciilogo);
        Console.ResetColor();

        var env = await IServerEnvironment.CreateDefaultAsync();

        var loggerProvider = new LoggerProvider(env.Configuration.LogLevel);
        var startupLogger = loggerProvider.CreateLogger("Startup");

        startupLogger.LogInformation("A C# implementation of the Minecraft server protocol. Targeting: {description}", Server.DefaultProtocol.GetDescription());

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(options =>
            {
                options.ClearProviders();
                options.AddProvider(loggerProvider);
                options.SetMinimumLevel(env.Configuration.LogLevel);
                //  Shhh... Only let Microsoft log when stuff crashes.
                //options.AddFilter("Microsoft", LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services.AddObsidian(env);

                // Give the server some time to shut down after CTRL-C or SIGTERM.
                services.Configure<HostOptions>(
                    opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(10));
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

