using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Obsidian.Plugins;

public sealed class PluginContainer : IDisposable
{
    private Type? pluginType;

    public IServiceScope ServiceScope { get; internal set; } = default!;

    public PluginBase? Plugin { get; private set; }
    public PluginInfo Info { get; }

    public AssemblyLoadContext? LoadContext { get; private set; }
    public Assembly PluginAssembly { get; } = default!;

    public string Source { get; internal set; } = default!;
    public bool HasDependencies { get; private set; } = true;
    public bool IsReady => HasDependencies;
    public bool Loaded { get; internal set; }

    public ImmutableArray<PluginFileEntry> FileEntries { get; }

    public PluginContainer(PluginBase plugin, PluginInfo info, Assembly assembly, AssemblyLoadContext loadContext, 
        string source, IEnumerable<PluginFileEntry> fileEntries)
    {
        Plugin = plugin;
        Info = info;
        LoadContext = loadContext;
        Source = source;
        PluginAssembly = assembly;
        pluginType = plugin.GetType();
        Plugin.Info = Info;
        this.FileEntries = fileEntries.ToImmutableArray();
    }

    /// <summary>
    /// Searches for the specified file.
    /// </summary>
    /// <param name="fileName">The name of the file you're searching for.</param>
    /// <returns>Null if the file is not found or the byte array of the file.</returns>
    public async Task<byte[]?> GetFileDataAsync(string fileName)
    {
        var fileEntry = this.FileEntries.FirstOrDefault(x => Path.GetFileName(x.FullName) == fileName);
        if (fileEntry is null)
            return null;

        await using var fs = new FileStream(this.Source, FileMode.Open);

        fs.Seek(fileEntry.Offset, SeekOrigin.Begin);

        var data = new byte[fileEntry.CompressedLength];

        await fs.ReadAsync(data);

        return data;
    }

    /// <summary>
    /// Inject the scoped services into 
    /// </summary>
    public void InjectServices(ILogger? logger, object? target = null)
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
                logger?.LogError(ex, "Failed to inject service.");
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
