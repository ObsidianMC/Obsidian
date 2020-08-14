using Obsidian.Concurrency;
using Obsidian.Logging;
using Obsidian.Plugins.Obsidian;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Plugins
{
    public class PluginManager
    {
        public PluginSource[] Sources = {
            new ObsidianPluginSource()
        };
        
        public ConcurrentHashSet<Plugin> Plugins { get; }
        
        public Server Server { get; }
        
        private string Path => System.IO.Path.Combine(Server.Path, "plugins");

        internal PluginManager(Server server)
        {
            this.Server = server;
            this.Plugins = new ConcurrentHashSet<Plugin>();
        }

        internal async Task LoadPluginsAsync(AsyncLogger logger)
        {
            if (!Directory.Exists(Path))
                Directory.CreateDirectory(Path);

            var discoveredPlugins = new List<Plugin>();

            foreach (var source in this.Sources)
                discoveredPlugins.AddRange(await source.GetPluginsAsync(Path));

            foreach (var plugin in discoveredPlugins)
            {
                await plugin.LoadAsync(Server);

                var authors = string.Join(", ", plugin.Info.Authors);
                await logger.LogMessageAsync($"Loaded plugin: {plugin.Info.Name} by {authors}");

                Plugins.Add(plugin);


            }
        }
    }
}