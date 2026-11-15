using Microsoft.Extensions.DependencyInjection;

namespace Obsidian.API.Plugins;

/// <summary>
/// Provides the base class for a plugin.
/// </summary>
public abstract class PluginBase : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the plugin's metadata. It is set by the server before <see cref="ConfigureServices"/> is called.
    /// </summary>
    public IPluginInfo Info { get; internal set; } = default!;

    /// <summary>
    /// Gets the server's handle on this plugin, with its services and packed files. It is set by the server before
    /// <see cref="ConfigureServices"/> is called.
    /// </summary>
    public IPluginContainer Container { get; internal set; } = default!;

    /// <summary>
    /// Used for registering services.
    /// </summary>
    /// <remarks>
    /// Only services from the Server will be injected when this method is called. e.x (ILogger, IServerConfiguration).
    /// Services registered through this method will be availiable/injected when <seealso cref="OnServerReadyAsync(IServer)"/> is called.
    /// </remarks>
    public virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    /// Used for registering commands, events, blocks, items and entities.
    /// </summary>
    /// <param name="pluginRegistry"></param>
    /// <remarks>
    /// Services from the Server will be injected when this method is called. e.x (ILogger, IServerConfiguration).
    /// Services registered through this method will be availiable/injected when <seealso cref="OnServerReadyAsync(IServer)"/> is called.
    /// </remarks>
    public virtual void ConfigureRegistry(IPluginRegistry pluginRegistry) { }


    /// <summary>
    /// Called when the world has loaded and the server is joinable.
    /// </summary>
    public virtual ValueTask OnServerReadyAsync(IServer server) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when the plugin has fully loaded.
    /// </summary>
    public virtual ValueTask OnLoadedAsync(IServer server) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when the plugin is being unloaded.
    /// </summary>
    public virtual ValueTask OnUnloadingAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public override sealed bool Equals(object? obj) => base.Equals(obj);
    /// <inheritdoc />
    public override sealed int GetHashCode() => base.GetHashCode();
    /// <summary>
    /// Returns the plugin's name, or its type's name before <see cref="Info"/> is set.
    /// </summary>
    public override sealed string ToString() => Info?.Name ?? GetType().Name;

    /// <summary>
    /// Releases the plugin's resources. The server calls <see cref="DisposeAsync"/>, which calls this by default, after
    /// <see cref="OnUnloadingAsync"/> when the plugin unloads.
    /// </summary>
    public virtual void Dispose() { }
    /// <summary>
    /// Releases the plugin's resources asynchronously. The server calls it after <see cref="OnUnloadingAsync"/> when the
    /// plugin unloads; by default it calls <see cref="Dispose"/>.
    /// </summary>
    public virtual ValueTask DisposeAsync()
    {
        try
        {
            this.Dispose();
            return default;
        }
        catch (Exception ex)
        {
            return ValueTask.FromException(ex);
        }
    }
}
