using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Obsidian.Plugins.Obsidian
{
    public class ObsidianPluginSource : PluginSource
    {
        public override IEnumerable<Plugin> GetPlugins(string path)
        {
            var files = Directory.GetFiles(path, "*.dll");
            foreach (var file in files) // don't touch pls
            {
                var assembly = Assembly.LoadFile(file);
                var classes = assembly.GetTypes().Where(type => typeof(IObsidianPluginClass).IsAssignableFrom(type) && type != typeof(IObsidianPluginClass));

                foreach (var type in classes)
                {
                    var pluginClass = (IObsidianPluginClass)Activator.CreateInstance(type);
                    var plugin = new ObsidianPlugin(this, file, pluginClass);

                    yield return plugin;
                }
            }
        }

        public override async Task<IEnumerable<Plugin>> GetPluginsAsync(string path) => GetPlugins(path);
    }
}