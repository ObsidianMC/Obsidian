﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using System.Threading.Tasks;

namespace SamplePlugin
{
    [Plugin(name: "Sample Plugin", Version = "1.0",
            Authors = "Obsidian Team", Description = "My sample plugin.",
            ProjectUrl = "https://github.com/Naamloos/Obsidian")]
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
        public override void ConfigureRegistry(IPluginConfigurationManager pluginConfiguration)
        {
            //Will scan for command classes and register them for you
            pluginConfiguration.MapCommands();

            //Will scan for classes that inherit from MinecraftEventHandler
            pluginConfiguration.MapEvents();

            //Want to make a simple command?? Here you go
            pluginConfiguration.MapCommand((CommandContext ctx) =>
            {

                return ValueTask.CompletedTask;
            });

            //Event doesn't need its own class? Here you go
            pluginConfiguration.MapEvent((IncomingChatMessageEventArgs chat) =>
            {

                return ValueTask.CompletedTask;
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

    [CommandRoot]
    public class MyCommands
    {
        [Inject]
        public SamplePlugin Plugin { get; set; }

        [Inject]
        public ILogger Logger { get; set; }

        [Command("mycommand")]
        [CommandInfo("woop dee doo this command is from a plugin")]
        public async Task MyCommandAsync(CommandContext ctx)
        {
            Plugin.Logger.LogInformation("Testing Plugin as injected dependency");
            Logger.LogInformation("Testing Services as injected dependency");
            await ctx.Player.SendMessageAsync("Hello from plugin command!");
        }
    }
}
