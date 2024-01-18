using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using System;
using System.Threading.Tasks;

namespace SamplePlugin
{
    public class SamplePlugin : PluginBase
    {
        // Dependencies will be injected automatically, if dependency class and field/property names match
        // Plugins won't load until all their required dependencies are added
        // Optional dependencies may be injected at any time, if at all
        [Inject]
        public ILogger<SamplePlugin> Logger { get; set; }

        //You can register services, commands and events here if you'd like
        public override void ConfigureServices(IServiceCollection services) { }


        //YOu can register commands, events and soon to be items, blocks entities
        public override void ConfigureRegistry(IPluginRegistry registry)
        {
            //Will scan for command classes and register them for you
            registry.MapCommands();

            //Will scan for classes that inherit from MinecraftEventHandler
            registry.MapEvents();

            //Want to make a simple command?? Here you go
            registry.MapCommand("test", 
                [CommandInfo("test command")]
                async (CommandContext ctx) =>
                {
                    await ctx.Player.SendMessageAsync("Test");
                });

            //Event doesn't need its own class? Here you go
            registry.MapEvent((IncomingChatMessageEventArgs chat) =>
            {

               
            });
        }

        //Called when the world has loaded and the server is ready for connections
        public override ValueTask OnLoadAsync(IServer server)
        {
            Logger.LogInformation("§a{pluginName} §floaded! Hello §aEveryone§f!", Info.Name);
            return ValueTask.CompletedTask;
        }

        [Command(commandName: "plugincommand")]
        [CommandInfo(description: "woop dee doo this command is from within a plugin class!!")]
        public async Task PluginCommandAsync(CommandContext ctx)
        {
            await ctx.Sender.SendMessageAsync(message: "Hello from plugin command implemented in Plugin class!");
        }
    }

    public class MyCommands : CommandModuleBase
    {
        [Inject]
        public ILogger Logger { get; set; }

        [Command("mycommand")]
        [CommandInfo("woop dee doo this command is from a plugin")]
        public async Task MyCommandAsync()
        {
            Logger.LogInformation("Testing Services as injected dependency");
            await this.Player.SendMessageAsync("Hello from plugin command!");
        }
    }
}
