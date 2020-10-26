using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Obsidian.Plugins.PluginProviders
{
    public class CompiledPluginProvider : IPluginProvider
    {
        public PluginContainer GetPlugin(string path, ILogger logger)
        {
            var loadContext = new PluginLoadContext(Path.GetFileNameWithoutExtension(path) + "LoadContext", path);
            var assembly = loadContext.LoadFromAssemblyPath(path);

            return HandlePlugin(loadContext, assembly, path, logger);
        }

        internal PluginContainer HandlePlugin(PluginLoadContext loadContext, Assembly assembly, string path, ILogger logger)
        {
            Type pluginType = assembly.GetTypes().FirstOrDefault(type => type.IsSubclassOf(typeof(PluginBase)));

            PluginBase plugin;
            if (pluginType == null || pluginType.GetConstructor(Array.Empty<Type>()) == null)
            {
                plugin = default;
                logger?.LogError("Loaded assembly contains no type implementing PluginBase with public parameterless constructor.");
                return new PluginContainer(new PluginInfo(Path.GetFileNameWithoutExtension(path)), path);
            }
            else
            {
                logger?.LogInformation("Creating plugin instance...");
                plugin = (PluginBase)Activator.CreateInstance(pluginType);
            }

            string name = assembly.GetName().Name;
            var attribute = pluginType.GetCustomAttribute<PluginAttribute>();
            var info = attribute != null ? new PluginInfo(name, attribute) : new PluginInfo(name);

            if (attribute == null)
                logger?.LogWarning($"Plugin is missing {nameof(PluginAttribute)}. Name defaults to '{info.Name}', version defaults to {info.Version}.");

            return new PluginContainer(plugin, info, assembly, loadContext, path);
        }
    }
}
