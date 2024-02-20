using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Plugins.PluginProviders;

public sealed class CompiledPluginProvider(ILogger logger) : IPluginProvider
{
    private readonly ILogger logger = logger;

    public async Task<PluginContainer> GetPluginAsync(string path)
    {
        var loadContext = new PluginLoadContext(Path.GetFileNameWithoutExtension(path) + "LoadContext", path);
        using var pluginStream = new FileStream(path, FileMode.Open);

        var assembly = loadContext.LoadFromStream(pluginStream);

        return await HandlePluginAsync(loadContext, assembly, path);
    }

    internal async Task<PluginContainer> HandlePluginAsync(PluginLoadContext loadContext, Assembly assembly, string path)
    {
        Type? pluginType = assembly.GetTypes().FirstOrDefault(type => type.IsSubclassOf(typeof(PluginBase)));

        PluginBase? plugin;
        if (pluginType == null || pluginType.GetConstructor([]) == null)
        {
            plugin = default;
            logger.LogError("Loaded assembly contains no type implementing PluginBase with public parameterless constructor.");
            return new PluginContainer(new PluginInfo(Path.GetFileNameWithoutExtension(path)), path);
        }
        else
        {
            logger.LogInformation("Creating plugin instance...");
            plugin = (PluginBase)Activator.CreateInstance(pluginType)!;
        }

        string name = assembly.GetName().Name!;
        using var pluginInfoStream = assembly.GetManifestResourceStream($"{name}.plugin.json") 
            ?? throw new InvalidOperationException($"Failed to find embedded plugin.json file for {name}");

        var info = await pluginInfoStream.FromJsonAsync<PluginInfo>() ?? throw new JsonException($"Couldn't deserialize plugin.json from {name}");

        return new PluginContainer(plugin, info, assembly, loadContext, path);
    }
}
