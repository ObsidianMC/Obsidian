using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Events;
using Obsidian.API.Plugins.Services;
using System;
using System.Threading.Tasks;

namespace SamplePlugin
{
    /// <summary>
    /// Class implementing the plugin functionality
    /// </summary>
    /// <remarks>Your plugin file(assembly/project) can have multiple of those, the server will load them all</remarks>
    public class SamplePlugin : Plugin
    {
        /// <summary>
        /// Unique identifier for the plugin, it should be of the format <code>tld.website.PluginName</code> (just like in Java)
        /// <para />
        /// If you don't have a website you can use <code>io.github.githubUsername.PluginName</code>
        /// </summary>
        public override string Identifier => "org.example.SamplePlugin";

        /// <summary>
        /// The information about your plugin. It contains its name, description, version, authors and an optional Url (like a page or a source repository)
        /// </summary>
        public override PluginInfo Info { get; } = new() {
            Name = "Sample Plugin",
            Description = "A plugin to test the functionality of the plugin system",
            Version = new Version(1, 0, 0, 0),
            Authors = new[] {"Obsidian Team"},
            ProjectUrl = new Uri("https://github.com/ObsidianMC/Obsidian")
        };

        private ILogger logger;
        private IServer server;
        
        /// <summary>
        /// Method used to initialize your plugin
        /// </summary>
        /// <param name="serviceProvider">Class that can provide you with built-in services and services registered by other plugins</param>
        public override void Initialize(IPluginServiceProvider serviceProvider)
        {
            logger = serviceProvider.GetLogger(this);
            server = serviceProvider.GetServer();

            logger.Log("Hello world!");
            logger.Log($"§a{Info.Name} §floaded!");
        }

        /// <summary>
        /// Method used to de-initialize your plugin
        /// <para />
        /// Make sure to dispose of any objects here, the server currently doesn't recognize whether your plugin
        /// implements <see cref="IDisposable"/> or <see cref="IAsyncDisposable"/>
        /// </summary>
        public override void DeInitialize()
        {
            logger.Log("Goodbye cruel world!");
        }

        /*
         All events must be decorated with EventHandler attribute
         
         The format is as follows:
         
         [EventHandler(Event.EventName, Priority = EventPriority.Priority)]
         public/private (static) (async Task)/void Name(EventArgs class corresponding to that event)
         {
            >Implementation<
         }
         
         The event will be automatically registered after initializing the plugin
            and unregistered before de-initializing the plugin         
         */
        
        
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
