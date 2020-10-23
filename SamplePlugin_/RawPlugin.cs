using Obsidian.Plugins.API;
using Obsidian.Plugins.API.Services;

namespace SamplePlugin_
{
    public class RawPlugin : PluginBase
    {
        [Inject] public ILogger Logger { get; set; }

        public void OnLoad()
        {
            Logger.Log("HELLO FROM RAW PLUGIN!!!");
        }
    }
}
