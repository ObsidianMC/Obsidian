using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;

namespace SamplePlugin
{
    public class RawPlugin : PluginBase
    {
        [Inject] public ILogger Logger { get; set; }

        public void OnLoad()
        {
            Logger.Log("Hello from Raw Plugin!");
        }
    }
}
