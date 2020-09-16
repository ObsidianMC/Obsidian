using Microsoft.Extensions.Logging;
using Obsidian.Concurrency;
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
        
        private string pluginPath => Path.Combine(Server.ServerFolderPath, "plugins");

        internal PluginManager(Server server)
        {
            this.Server = server;
            this.Plugins = new ConcurrentHashSet<Plugin>();
        }

        internal async Task LoadPluginsAsync(ILogger logger)
        {
            if (!Directory.Exists(this.pluginPath))
                Directory.CreateDirectory(this.pluginPath);

            var discoveredPlugins = new List<Plugin>();

            foreach (var source in this.Sources)
                discoveredPlugins.AddRange(await source.GetPluginsAsync(this.pluginPath));

            foreach (var plugin in discoveredPlugins)
            {
                await plugin.LoadAsync(Server);

                var authors = string.Join(", ", plugin.Info.Authors);
                logger.LogInformation($"Loaded plugin: {plugin.Info.Name} by {authors}");

                this.Plugins.Add(plugin);
            }
        }
    }
}