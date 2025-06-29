using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Obsidian;
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

await GenerateConfigFiles();

var builder = Host.CreateApplicationBuilder();

builder.ConfigureObsidian();

if(!Directory.Exists("logs"))
{
    Directory.CreateDirectory("logs");
}

builder.AddObsidian();

// Give the server some time to shut down after CTRL-C or SIGTERM.
builder.Services.Configure<HostOptions>(opts =>
{
    opts.ShutdownTimeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

await app.RunAsync();
