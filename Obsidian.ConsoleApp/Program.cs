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
// filename with date,time
var logFile = $"logs/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
var logFileStream = new FileStream(logFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();

    //Console logger can be edited through server.config https://learn.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter
    loggingBuilder.AddSimpleConsole(x =>
    {
        x.ColorBehavior = LoggerColorBehavior.Enabled;
        x.SingleLine = true;
        x.IncludeScopes = true;
        x.TimestampFormat = "HH:mm:ss ";
    });

    loggingBuilder.AddProvider(new StreamLoggerProvider(logFileStream));
});

builder.AddObsidian();

// Give the server some time to shut down after CTRL-C or SIGTERM.
builder.Services.Configure<HostOptions>(opts =>
{
    opts.ShutdownTimeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

await app.RunAsync();
