using Microsoft.Extensions.DependencyInjection;
using Obsidian.API.Events;
using System.IO;

namespace Obsidian.API.Plugins;

/// <summary>
/// Provides the base class for a plugin.
/// </summary>
public abstract class PluginBase : IDisposable, IAsyncDisposable
{
#nullable disable
    public IPluginInfo Info { get; internal set; }

    internal Action unload;
#nullable restore

    private Type typeCache;

    public PluginBase()
    {
        typeCache ??= GetType();
    }

    /// <summary>
    /// Used for registering services.
    /// </summary>
    /// <remarks>
    /// Only services from the Server will be injected when this method is called. e.x (ILogger, IServerConfiguration).
    /// Services registered through this method will be availiable/injected when <seealso cref="OnLoadAsync(IServer)"/> is called.
    /// </remarks>
    public virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    /// Used for registering commands, events, blocks, items and entities.
    /// </summary>
    /// <param name="pluginConfiguration"></param>
    /// <remarks>
    /// Services from the Server will be injected when this method is called. e.x (ILogger, IServerConfiguration).
    /// Services registered through this method will be availiable/injected when <seealso cref="OnLoadAsync(IServer)"/> is called.
    /// </remarks>
    public virtual void ConfigureRegistry(IPluginConfigurationManager pluginConfiguration)
    {
        //Will scan for command classes and register them for you
        pluginConfiguration.MapCommands();

        //Will scan for classes that inherit from MinecraftEventHandler
        pluginConfiguration.MapEvents();

        //Want to make a simple command?? Here you go
        pluginConfiguration.MapCommand((CommandContext ctx) =>
        {

            return ValueTask.CompletedTask;
        });

        //Event doesn't need its own class? Here you go
        pluginConfiguration.MapEvent((IncomingChatMessageEventArgs chat) =>
        {

            return ValueTask.CompletedTask;
        });
    }


    /// <summary>
    /// Called when the world has loaded and the server is joinable.
    /// </summary>
    public virtual ValueTask OnLoadAsync(IServer server) => ValueTask.CompletedTask;


    /// <summary>
    /// Causes this plugin to be unloaded.
    /// </summary>
    protected void Unload()
    {
        unload();
    }

    public override sealed bool Equals(object? obj) => base.Equals(obj);
    public override sealed int GetHashCode() => base.GetHashCode();
    public override sealed string ToString() => Info?.Name ?? GetType().Name;

    public virtual void Dispose() { }
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
