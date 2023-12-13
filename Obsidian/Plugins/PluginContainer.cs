using Obsidian.API.Plugins;
using System.Reflection;
using System.Runtime.Loader;

namespace Obsidian.Plugins;

public sealed class PluginContainer : IDisposable
{
    private Type? pluginType;

    public PluginBase Plugin { get; private set; }
    public PluginInfo Info { get; }
    public string Source { get; internal set; } = default!;
    public AssemblyLoadContext LoadContext { get; private set; }

    public Assembly PluginAssembly { get; } = default!;
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

    public void Dispose()
    {
        Plugin = null;
        LoadContext = null;
        pluginType = null;
    }
}
