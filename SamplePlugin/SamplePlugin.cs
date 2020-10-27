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

        // Dependencies will be injected automatically, if dependency class and field/property names match
        // Plugins won't load until all their required dependencies are added
        // Optional dependencies may be injected at any time, if at all
        [Dependency(MinVersion = "2.0", Optional = true), Alias("Sample Remote Plugin")]
        public MyWrapper SampleRemotePlugin { get; set; }

        // One of server messages, called when an event occurs
        public async Task OnLoad()
        {
            Logger.Log("Sample plugin loaded!");
            await Task.CompletedTask;
        }

        public async Task OnServerTick()
        {
            SampleRemotePlugin.Step();
            if (SampleRemotePlugin.StepCount % 300 == 0)
                Logger.Log($"Step {SampleRemotePlugin.StepCount}!");
            await Task.CompletedTask;
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
