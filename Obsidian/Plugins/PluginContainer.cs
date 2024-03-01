using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Obsidian.Plugins;

public sealed class PluginContainer : IDisposable
{
    private Type PluginType => this.Plugin.GetType();
    public IServiceScope ServiceScope { get; internal set; } = default!;
    public PluginInfo Info { get; private set; } = default!;

    [AllowNull]
    public PluginBase Plugin { get; internal set; } = default!;

    [AllowNull]
    public AssemblyLoadContext LoadContext { get; internal set; } = default!;

    [AllowNull]
    public Assembly PluginAssembly { get; internal set; } = default!;

    [AllowNull]
    public FrozenDictionary<string, PluginFileEntry> FileEntries { get; internal set; } = default!;
    public required string Source { get; set; }
 
    public bool HasDependencies { get; private set; } = true;
    public bool IsReady => HasDependencies;
    public bool Loaded { get; internal set; }

    ~PluginContainer()
    {
        this.Dispose(false);
    }

    internal async Task InitializeAsync()
    {
        var pluginJsonData = await this.GetFileDataAsync("plugin.json") ?? throw new InvalidOperationException("Failed to find plugin.json");

        await using var pluginInfoStream = new MemoryStream(pluginJsonData, false);
        this.Info = await pluginInfoStream.FromJsonAsync<PluginInfo>() ?? throw new NullReferenceException("Failed to deserialize plugin.json");
        this.Plugin.Info = this.Info;
    }

    /// <summary>
    /// Searches for the specified file.
    /// </summary>
    /// <param name="fileName">The name of the file you're searching for.</param>
    /// <returns>Null if the file is not found or the byte array of the file.</returns>
    public async Task<byte[]?> GetFileDataAsync(string fileName)
    {
        var fileEntry = this.FileEntries.GetValueOrDefault(fileName);
        if (fileEntry is null)
            return null;

        await using var fs = new FileStream(this.Source, FileMode.Open, FileAccess.Read, FileShare.Read);

        return await fileEntry.GetDataAsync(fs);
    }

    /// <summary>
    /// Inject the scoped services into 
    /// </summary>
    public void InjectServices(ILogger? logger, object? target = null)
    {
        var properties = target is null ? this.PluginType!.WithInjectAttribute() : target.GetType().WithInjectAttribute();

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
        this.Dispose(true);

        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if(this.ServiceScope != null)
            this.ServiceScope.Dispose();

        if (disposing)
        {
            this.PluginAssembly = null;
            this.LoadContext = null;
            this.Plugin = null;
            this.FileEntries = null;
        }
    }
}
