using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using System;
using System.Threading.Tasks;

namespace SamplePlugin
{
    [Plugin(Name = "Sample Plugin", Version = "1.0",
            Authors = "Obsidian Team", Description = "My sample plugin.",
            ProjectUrl = "https://github.com/Naamloos/Obsidian")]
    public class SamplePlugin : PluginBase
    {
        // Any interface from Obsidian.Plugins.Services can be injected into properties
        [Inject] public ILogger Logger { get; set; }
        [Inject] public IFileReader FileReader { get; set; }

        // Dependencies will be injected automatically, if dependency class and field/property names match
        // Plugins won't load until all their required dependencies are added
        // Optional dependencies may be injected at any time, if at all
        [Dependency(MinVersion = "2.0", Optional = true), Alias("Sample Remote Plugin")]
        public MyWrapper SampleRemotePlugin { get; set; }

        // One of server messages, called when an event occurs
        public async Task OnLoad(IServer server)
        {
            Logger.Log($"§a{Info.Name} §floaded! Hello §a{server.DefaultWorld.Name}§f!");
            Logger.Log($"Hello! I live at §a{FileReader.CreateWorkingDirectory()}§f!");

            var commanddependencies = new CommandDependencyBundle();
            await commanddependencies.RegisterDependencyAsync(Logger);
            this.RegisterCommandDependencies(commanddependencies);

            await Task.CompletedTask;
        }

        public async Task OnPermissionRevoked(PermissionRevokedEventArgs args)
        {
            Logger.Log($"Permission {args.Permission} revoked from player {args.Player.Username}");
            await Task.CompletedTask;
        }

        public async Task OnPermissionGranted(PermissionGrantedEventArgs args)
        {
            Logger.Log($"Permission {args.Permission} granted to player {args.Player.Username}");
            await Task.CompletedTask;
        }

        public async Task OnPlayerJoin(PlayerJoinEventArgs playerJoinEvent)
        {
            var player = playerJoinEvent.Player;

            await player.SendMessageAsync(IChatMessage.Simple($"Welcome {player.Username}!", ChatColor.Gold));
        }

        [Command("plugincommand")]
        [CommandInfo("woop dee doo this command is from within a plugin class!!")]
        public async Task PluginCommandAsync(CommandContext ctx)
        {
            await ctx.Player.SendMessageAsync("Hello from plugin command implemented in Plugin class!");
        }
    }

    [CommandRoot]
    public class MyCommands
    {
        public ILogger logger;
        public MyCommands(ILogger l)
        {
            this.logger = l;
        }

        [Command("mycommand")]
        [CommandInfo("woop dee doo this command is from a plugin")]
        public async Task MyCommandAsync(CommandContext ctx)
        {
            logger.Log("Logging via dependencies for command framework");
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
