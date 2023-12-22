using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian;
using Obsidian.API.Logging;
using Obsidian.Hosting;

// Cool startup console logo because that's cool
// 10/10 -IGN
const string asciilogo =
    "\n" +
    "      ▄▄▄▄   ▄▄       ▄▄▄▄  ▀   ▄▄▄   ▐ ▄ \n" +
    "      ▐█ ▀█ ▐█ ▀  ██ ██  ██ ██ ▐█ ▀█  █▌▐█\n" +
    " ▄█▀▄ ▐█▀▀█▄▄▀▀▀█▄▐█ ▐█  ▐█▌▐█ ▄█▀▀█ ▐█▐▐▌\n" +
    "▐█▌ ▐▌██▄ ▐█▐█▄ ▐█▐█▌██  ██ ▐█▌▐█  ▐▌██▐█▌\n" +
    " ▀█▄▀  ▀▀▀▀  ▀▀▀▀ ▀▀▀▀▀▀▀▀  ▀▀▀ ▀  ▀ ▀▀ █ \n\n";

Console.Title = $"Obsidian for {Server.DefaultProtocol} ({Server.VERSION})";
Console.BackgroundColor = ConsoleColor.White;
Console.ForegroundColor = ConsoleColor.Black;
Console.CursorVisible = false;
Console.WriteLine(asciilogo);
Console.ResetColor();

var env = await IServerEnvironment.CreateDefaultAsync();

var builder = Host.CreateApplicationBuilder();

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddProvider(new LoggerProvider(env.Configuration.LogLevel));
    loggingBuilder.SetMinimumLevel(env.Configuration.LogLevel);
});

builder.Logging.AddFilter((provider, category, logLevel) =>
{
    return !category.Contains("Microsoft") || logLevel != LogLevel.Debug;
});

builder.Services.AddObsidian(env);

// Give the server some time to shut down after CTRL-C or SIGTERM.
builder.Services.Configure<HostOptions>(opts =>
{
    opts.ShutdownTimeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

await app.RunAsync();
