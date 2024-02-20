using Microsoft.Extensions.Logging;

namespace Obsidian.Plugins.PluginProviders;

public interface IPluginProvider
{
    public Task<PluginContainer> GetPluginAsync(string path);
}
