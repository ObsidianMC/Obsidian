﻿using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using System;
using System.Threading.Tasks;

namespace SamplePlugin
{
    [Plugin(name: "Sample Plugin", Version = "1.0",
            Authors = "Obsidian Team", Description = "My sample plugin.",
            ProjectUrl = "https://github.com/Naamloos/Obsidian")]
    public class SamplePlugin : PluginBase
    {
        // Any interface from Obsidian.Plugins.Services can be injected into properties
        [Inject] 
        public ILogger Logger { get; set; }

        // Dependencies will be injected automatically, if dependency class and field/property names match
        // Plugins won't load until all their required dependencies are added
        // Optional dependencies may be injected at any time, if at all
        [Dependency(minVersion: "2.0", optional: true), Alias(identifier: "Sample Remote Plugin")]
        public MyWrapper SampleRemotePlugin { get; set; }

        // One of server messages, called when an event occurs

        public override ValueTask OnLoadAsync(IServer server)
        {
            Logger.LogInformation("§a{pluginName} §floaded! Hello §aEveryone§f!", Info.Name);
            return ValueTask.CompletedTask;
        }

        public void OnPermissionRevoked(PermissionRevokedEventArgs args)
        {
            Logger.LogInformation("Permission {permission} revoked from player {user}", args.Permission, args.Player.Username);
        }

        public void OnPermissionGranted(PermissionGrantedEventArgs args)
        {
            Logger.LogInformation("Permission {permission} granted to player {user}", args.Permission, args.Player.Username);
        }

        public async Task OnPlayerJoin(PlayerJoinEventArgs playerJoinEvent)
        {
            var player = playerJoinEvent.Player;

            await player.SendMessageAsync(message: ChatMessage.Simple(text: $"Welcome {player.Username}!", color: ChatColor.Gold));
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

    public class MyWrapper : PluginWrapper
    {
        public Action Step { get; set; }
        [Alias("get_StepCount")] private Func<int> GetStepCount { get; set; }
        [Alias("set_StepCount")] private Action<int> SetStepCount { get; set; }

        public int StepCount { get => GetStepCount(); set => SetStepCount(value); }
    }
}
