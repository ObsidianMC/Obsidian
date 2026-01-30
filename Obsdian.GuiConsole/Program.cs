using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.GuiConsole.Logger;
using Obsidian.GuiConsole.Window;
using Obsidian.Hosting;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;

namespace Obsidian.GuiConsole;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        //Cool logo,but not render well on my machine -- stevesensei
        DrawLogo();
        
        //Init Gui App
        ConfigurationManager.Enable(ConfigLocations.All);
        
        //Create console window
        var console = new ObsdianConsole();
        
        //Normal Obsidian setup
        await GenerateConfigFiles();
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureObsidian();
        if (!Directory.Exists("logs"))
        {
            Directory.CreateDirectory("logs");
        }
        
        //Add Obsidian with GUI logger
        builder.AddObsidianWithGui(x =>
        {
            x.AddProvider(new TerminalGuiLoggerProvider(console, LogLevel.Information));
            return x;
        });
        
        //Give the server some time to shut down after CTRL-C or SIGTERM.
        builder.Services.Configure<HostOptions>(opts =>
        {
            opts.ShutdownTimeout = TimeSpan.FromSeconds(10);
        });

        var hostApp = builder.Build();
        
        IApplication? app = null;
        try
        {
            //Run the application
            await hostApp.StartAsync();
            
            app = Application.Create().Init();
            app.Run(console);
        }
        finally
        {
            //Cleanup when application stops
            await hostApp.StopAsync();
            app?.Dispose();
        }
    }
}
