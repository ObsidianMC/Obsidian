using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Obsidian.Plugins;

public sealed class PluginContainer : IDisposable
{
    private Type? pluginType;

    public IServiceScope ServiceScope { get; internal set; } = default!;

    [AllowNull]
    public PluginBase Plugin { get; private set; }
    public PluginInfo Info { get; }

    [AllowNull]
    public AssemblyLoadContext LoadContext { get; private set; }
    public Assembly PluginAssembly { get; } = default!;

    public string Source { get; internal set; } = default!;
    public string ClassName { get; } = default!;

    public bool HasDependencies { get; private set; } = true;
    public bool IsReady => HasDependencies;
    public bool Loaded { get; internal set; }

    public PluginContainer(PluginInfo info, string source)
    {
        Info = info;
        Source = source;
    }

    public PluginContainer(PluginBase plugin, PluginInfo info, Assembly assembly, AssemblyLoadContext loadContext, string source)
    {

        Plugin = plugin;
        Info = info;
        LoadContext = loadContext;
        Source = source;
        PluginAssembly = assembly;

        pluginType = plugin.GetType();
        ClassName = pluginType.Name;
        Plugin.Info = Info;
    }

    /// <summary>
    /// Inject the scoped services into 
    /// </summary>
    public void InjectServices(ILogger logger, object? target = null)
    {
        var properties = target is null ? this.pluginType!.WithInjectAttribute() : target.GetType().WithInjectAttribute();

        target ??= this.Plugin;

        foreach (var property in properties)
        {
            try
            {
                var service = this.ServiceScope.ServiceProvider.GetRequiredService(property.PropertyType);

                property.SetValue(target, service);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to inject service.");
            }
        }

    }

    public void Dispose()
    {
        Plugin = null;
        LoadContext = null;
        pluginType = null;

        this.ServiceScope.Dispose();
    }
}
