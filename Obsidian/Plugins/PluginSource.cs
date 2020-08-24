using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Plugins
{
    public abstract class PluginSource
    {
        public abstract IEnumerable<Plugin> GetPlugins(string path);
        
        public abstract Task<IEnumerable<Plugin>> GetPluginsAsync(string path);
    }
}