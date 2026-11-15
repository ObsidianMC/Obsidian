using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Obsidian.API.Plugins;
/// <summary>
/// The server's handle on a loaded plugin: its assembly, its services and the files packed with it.
/// </summary>
public interface IPluginContainer
{
    /// <summary>
    /// Gets the plugin's instance, or <see langword="null"/> before it has been created.
    /// </summary>
    public PluginBase? Plugin { get; }
    /// <summary>
    /// Gets the plugin's service scope, which resolves the services the plugin registered and falls back to the server's.
    /// </summary>
    public IServiceScope ServiceScope { get; }
    /// <summary>
    /// Gets the assembly the plugin was loaded from.
    /// </summary>
    public Assembly PluginAssembly { get; }

    /// <summary>
    /// Sets every property of <paramref name="module"/> marked with <see cref="InjectAttribute"/> that is still
    /// <see langword="null"/> to the matching service from <see cref="ServiceScope"/>.
    /// </summary>
    /// <param name="logger">Logs services that couldn't be resolved.</param>
    /// <param name="module">The object whose properties to set.</param>
    public void InjectServices(ILogger logger, object module);

    /// <summary>
    /// Searches for the specified file that was packed alongside your plugin.
    /// </summary>
    /// <param name="fileName">The name of the file you're searching for.</param>
    /// <returns>Null if the file is not found or the byte array of the file.</returns>
    public byte[]? GetFileData(string fileName);
}
