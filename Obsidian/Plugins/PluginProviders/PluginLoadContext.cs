using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Obsidian.Plugins.PluginProviders;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    // Unload begins before escaped plugin tasks finish. Keep looking while their contexts are still reachable.
    private static readonly List<WeakReference<PluginLoadContext>> contexts = [];

    public PluginLoadContext(string name) : base(name: name, isCollectible: true)
    {
        lock (contexts)
            contexts.Add(new WeakReference<PluginLoadContext>(this));
    }

    /// <summary>
    /// Whether any plugin's code may be loaded, so that it's worth looking for it, as on a call stack.
    /// </summary>
    internal static bool AnyLoaded
    {
        get
        {
            lock (contexts)
            {
                contexts.RemoveAll(reference => !reference.TryGetTarget(out _));
                return contexts.Count > 0;
            }
        }
    }

    public List<PluginLoadContext> Dependencies { get; } = [];

    /// <summary>
    /// The plugin loaded in the context, to find the plugin that code from the context belongs to.
    /// </summary>
    internal PluginContainer? Container { get; set; }

    /// <summary>
    /// The plugin whose load context <paramref name="assembly"/> lives in, or null for the server's own code, so that
    /// an event handler or command registered without a container counts as the plugin's whose code it is.
    /// </summary>
    internal static PluginContainer? OwnerOf(Assembly assembly) =>
        (GetLoadContext(assembly) as PluginLoadContext)?.Container;

    public Assembly? LoadAssembly(byte[] mainBytes, byte[]? pbdBytes = null)
    {
        using var mainStream = new MemoryStream(mainBytes, false);
        using var pbdStream = pbdBytes is not null ? new MemoryStream(pbdBytes, false) : null;

        return this.LoadFromStream(mainStream, pbdStream);
    }

    public void AddDependency(PluginLoadContext context) => this.Dependencies.Add(context);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assembly = this.Assemblies.FirstOrDefault(x => x.FullName == assemblyName.FullName);

        return this.Dependencies.Select(x => x.Load(assemblyName)).FirstOrDefault(x => x != null) ?? assembly;
    }
}
