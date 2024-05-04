﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.Plugins.PluginProviders;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Obsidian.Plugins;

public sealed class PluginContainer : IDisposable, IPluginContainer
{
    private bool initialized;

    private Type? PluginType => this.Plugin?.GetType();

    public IServiceScope ServiceScope { get; internal set; } = default!;
    public PluginInfo Info { get; private set; } = default!;

    public PluginBase? Plugin { get; internal set; }

    [AllowNull]
    public PluginLoadContext LoadContext { get; internal set; } = default!;

    [AllowNull]
    public Assembly PluginAssembly { get; internal set; } = default!;

    [AllowNull]
    public FrozenDictionary<string, PluginFileEntry> FileEntries { get; internal set; } = default!;

    public required string Source { get; set; }
    public required bool ValidSignature { get; init; }

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

        this.Plugin!.Container = this;
        this.Plugin!.Info = this.Info;
    }

    //TODO PLUGINS SHOULD USE VERSION CLASS TO SPECIFY VERSION
    internal bool IsDependency(string pluginId) =>
        this.Info.Dependencies.Any(x => x.Id == pluginId);

    internal bool AddDependency(PluginLoadContext pluginLoadContext)
    {
        ArgumentNullException.ThrowIfNull(pluginLoadContext);

        if (this.LoadContext == null)
            return false;

        this.LoadContext.AddDependency(pluginLoadContext);
        return true;
    }

    /// <summary>
    /// Inject the scoped services into 
    /// </summary>
    internal void InjectServices(ILogger? logger, object? target = null)
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

    ///<inheritdoc/>
    public byte[]? GetFileData(string fileName)
    {
        var fileEntry = this.FileEntries?.GetValueOrDefault(fileName);

        return fileEntry?.GetData();
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
