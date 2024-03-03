using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.Plugins.PluginProviders;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Obsidian.Plugins;

public sealed class PluginContainer : IDisposable
{
    private bool initialized;

    private Type PluginType => this.Plugin.GetType();
    public IServiceScope ServiceScope { get; internal set; } = default!;
    public PluginInfo Info { get; private set; } = default!;

    [AllowNull]
    public PluginBase Plugin { get; internal set; } = default!;

    [AllowNull]
    public PluginLoadContext LoadContext { get; internal set; } = default!;

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

    internal void Initialize()
    {
        if (!this.initialized)
        {
            var pluginJsonData = this.GetFileData("plugin.json") ?? throw new InvalidOperationException("Failed to find plugin.json");

            this.Info = pluginJsonData.FromJson<PluginInfo>() ?? throw new NullReferenceException("Failed to deserialize plugin.json");

            this.initialized = true;

            return;
        }

        this.Plugin.Info = this.Info;
    }

    /// <summary>
    /// Searches for the specified file.
    /// </summary>
    /// <param name="fileName">The name of the file you're searching for.</param>
    /// <returns>Null if the file is not found or the byte array of the file.</returns>
    public byte[]? GetFileData(string fileName)
    {
        var fileEntry = this.FileEntries.GetValueOrDefault(fileName);

        return fileEntry?.GetData();
    }

    //TODO PLUGINS SHOULD USE VERSION CLASS TO SPECIFY VERSION
    public bool IsDependency(string pluginId) =>
        this.Info.Dependencies.Any(x => x.Id == pluginId);

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
        this.ServiceScope?.Dispose();

        if (disposing)
        {
            this.PluginAssembly = null;
            this.LoadContext = null;
            this.Plugin = null;
            this.FileEntries = null;
        }
    }
}
