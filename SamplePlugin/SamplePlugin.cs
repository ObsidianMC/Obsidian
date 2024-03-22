using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using System.Threading.Tasks;

namespace SamplePlugin;
 public sealed class SamplePlugin : PluginBase
{
    // Dependencies will be injected automatically, if dependency class and field/property names match
    // Plugins won't load until all their required dependencies are added
    // Optional dependencies may be injected at any time, if at all
    [Inject]
    public ILogger<SamplePlugin> Logger { get; set; }

    //You can register services, commands and events here if you'd like
    public override void ConfigureServices(IServiceCollection services) { }

    //You can register commands, events and soon to be items, blocks and entities
    public override void ConfigureRegistry(IPluginRegistry registry)
    {
        //Will scan for command classes and register them for you
        registry.MapCommands();

        //Will scan for classes that inherit from MinecraftEventHandler
        registry.MapEvents();

        //For those coming from the web side of .net these will seem familiar to you.
        //You're able to register commands through a "minimal api" like approach
        registry.MapCommand("test",
            [CommandInfo("test command")]
        async (CommandContext ctx, int number, int otherNumber) =>
            {
                await ctx.Player.SendMessageAsync($"Test #{number} and #{otherNumber}. This command was executed from the MinimalAPI.");
            });

        //As above so below :))
        registry.MapEvent((IncomingChatMessageEventArgs chat) =>
        {
            this.Logger.LogDebug("Got a chat message! From MinimalAPI event.");
        });
    }

    //Called when the plugin has fully loaded
    public override ValueTask OnLoadedAsync(IServer server)
    {
        Logger.LogInformation("§a{pluginName} §floaded! Hello §aEveryone§f!", Info.Name);

        return ValueTask.CompletedTask;
    }

    //Called when the world has loaded and the server is ready for connections
    public override ValueTask OnServerReadyAsync(IServer server)
    {
        Logger.LogInformation("Wow you can join the server!!");
        return ValueTask.CompletedTask;
    }

    //This is self explanatory (called when the plugin is being unloaded)
    public override ValueTask OnUnloadingAsync()
    {
        Logger.LogInformation("I'm unloading now :(");
        return ValueTask.CompletedTask;
    }
}

//All event handlers are created with a scoped lifetime
public class MyEventHandler : MinecraftEventHandler
{
    [EventPriority(Priority = Priority.Critical)]
    public async ValueTask ChatEvent(IncomingChatMessageEventArgs args)
    {
        await args.Player.SendMessageAsync("I got your chat message through event handler class!");
    }
}

//All command modules are created with a scoped lifetime
public class MyCommands : CommandModuleBase
{
    [Inject]
    public ILogger<MyCommands> Logger { get; set; }

    [Command("mycommand")]
    [CommandInfo("woop dee doo this command is from a plugin")]
    public async Task MyCommandAsync()
    {
        Logger.LogInformation("Testing Services as injected dependency");
        await this.Player.SendMessageAsync("Hello from plugin command!");
    }
}
