using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Events;
using Obsidian.API.Plugins.Services;
using System;
using System.Threading.Tasks;

namespace SamplePlugin
{
    public class SamplePlugin : Plugin
    {
        public override string Identifier => "org.example.SamplePlugin";

        public override PluginInfo Info { get; } = new() {
            Name = "Sample Plugin",
            Description = "A plugin to test the functionality of the plugin system",
            Version = new Version(1, 0, 0, 0),
            Authors = new[] {"Obsidian Team"},
            ProjectUrl = new Uri("https://github.com/ObsidianMC/Obsidian")
        };

        private ILogger logger;
        private IServer server;
        
        public override void Initialize(IPluginServiceProvider serviceProvider)
        {
            logger = serviceProvider.GetLogger(this);
            server = serviceProvider.GetServer();

            logger.Log("Hello world!");
            logger.Log($"§a{Info.Name} §floaded!");
        }

        public override void DeInitialize()
        {
            logger.Log("Goodbye cruel world!");
        }

        [EventHandler(Event.PermissionRevoked)]
        public void OnPermissionRevoked(PermissionRevokedEventArgs args)
        {
            logger.Log($"Permission {args.Permission} revoked from player {args.Player.Username}");
        }

        [EventHandler(Event.PermissionGranted, EventPriority.Critical)]
        public void OnPermissionGranted(PermissionGrantedEventArgs args)
        {
            logger.Log($"Permission {args.Permission} granted to player {args.Player.Username}");
        }

        [EventHandler(Event.PlayerJoin)]
        public async Task OnPlayerJoinedAsync(PlayerJoinEventArgs args)
        {
            var player = args.Player;

            await player.SendMessageAsync(ChatMessage.Simple($"Welcome {player.Username}!", ChatColor.Gold));
        }
    }
}
