using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

Console.Title = $"Obsidian for {ServerConstants.DefaultProtocol} ({ServerConstants.VERSION})";
Console.BackgroundColor = ConsoleColor.White;
Console.ForegroundColor = ConsoleColor.Black;
Console.WriteLine(asciilogo);
Console.ResetColor();

var builder = Host.CreateApplicationBuilder(args);

// The configuration files are generated under Paths:Root before Obsidian reads them. An integrated server runs on its own
// defaults (see ConfigureObsidian), which the dedicated server's files would override.
ServerConstants.ConfigurePaths(builder.Configuration);
if (!builder.Configuration.GetValue<bool>("Integrated:Enabled"))
    await GenerateConfigFiles(ServerConstants.ConfigPath);

builder.ConfigureObsidian();
builder.AddObsidian();

// Give the server some time to shut down after CTRL-C or SIGTERM.
builder.Services.Configure<HostOptions>(opts =>
{
    opts.ShutdownTimeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

await app.RunAsync();
