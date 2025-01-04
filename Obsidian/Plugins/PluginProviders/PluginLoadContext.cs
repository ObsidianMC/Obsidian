using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Obsidian.Plugins.PluginProviders;

public sealed class PluginLoadContext(string name) : AssemblyLoadContext(name: name, isCollectible: true)
{
    public List<PluginLoadContext> Dependencies { get; } = [];

    public Assembly? LoadAssembly(byte[] mainBytes, byte[]? pbdBytes = null)
    {
        using var mainStream = new MemoryStream(mainBytes, false);
        using var pbdStream = pbdBytes != null ? new MemoryStream(pbdBytes, false) : null;

        return this.LoadFromStream(mainStream, pbdStream);
    }

    public void AddDependency(PluginLoadContext context) => this.Dependencies.Add(context);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assembly = this.Assemblies.FirstOrDefault(x => x.FullName == assemblyName.FullName);

        return this.Dependencies.Select(x => x.Load(assemblyName)).FirstOrDefault(x => x != null) ?? assembly;
    }
}
