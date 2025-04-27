using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Obsidian.API.Plugins;
public interface IPluginContainer
{
    public PluginBase? Plugin { get; }
    public IServiceScope ServiceScope { get; }
    public Assembly PluginAssembly { get; }

    public void InjectServices(ILogger logger, object module);

    /// <summary>
    /// Searches for the specified file that was packed alongside your plugin.
    /// </summary>
    /// <param name="fileName">The name of the file you're searching for.</param>
    /// <returns>Null if the file is not found or the byte array of the file.</returns>
    public byte[]? GetFileData(string fileName);
}
