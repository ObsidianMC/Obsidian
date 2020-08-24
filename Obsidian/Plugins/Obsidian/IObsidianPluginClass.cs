using System.Threading.Tasks;

namespace Obsidian.Plugins.Obsidian
{
    // Plugins derive from this base class.
    public interface IObsidianPluginClass
    {
        /// <summary>
        /// The information about a plugin which will be represented in plugin listings
        /// </summary>
        PluginInfo Info { get; }
        
        /// <summary>
        /// Initializes a plugin, and returns a new PluginInfo object.
        /// </summary>
        /// <param name="server">Server to intialize plugin in</param>
        Task InitializeAsync(Server server);
    }
}