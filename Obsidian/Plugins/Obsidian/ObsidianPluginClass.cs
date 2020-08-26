using System.Threading.Tasks;

namespace Obsidian.Plugins.Obsidian
{
    public abstract class ObsidianPluginClass
    {
        public PluginInfo Info { get; set; }
        public Server Server { get; internal set; }

        /// <summary>
        /// Initialize all your stuff here
        /// </summary>
        public abstract Task InitializeAsync();
    }
}