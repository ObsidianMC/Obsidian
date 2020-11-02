using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
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

        // Dependencies will be injected automatically, if dependency class and field/property names match
        // Plugins won't load until all their required dependencies are added
        // Optional dependencies may be injected at any time, if at all
        [Dependency(MinVersion = "2.0", Optional = true), Alias("Sample Remote Plugin")]
        public MyWrapper SampleRemotePlugin { get; set; }

        // One of server messages, called when an event occurs
        public async Task OnLoad(IServer server)
        {
            Logger.Log($"Sample plugin loaded! Server version: {server.Version}");
            server.RegisterCommandClass<MyCommands>();
            await Task.CompletedTask;
        }

        public async Task OnServerTick()
        {
            SampleRemotePlugin.Step();
            if (SampleRemotePlugin.StepCount % 1000 == 0)
                Logger.Log($"Reached {SampleRemotePlugin.StepCount} ticks!");
            await Task.CompletedTask;
        }

        public async Task OnPlayerJoin(PlayerJoinEventArgs playerJoinEvent)
        {
            var player = playerJoinEvent.Player;
            var server = playerJoinEvent.Server;

            await player.SendMessageAsync("Welcome to the server!");
        }

        public class MyCommands : BaseCommandClass
        {
            [Command("mycommand")]
            [CommandInfo("woop dee doo this command is from a plugin")]
            public async Task MyCommandAsync(ObsidianContext ctx)
            {
                await ctx.Player.SendMessageAsync("Hello from plugin command!");
            }
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
