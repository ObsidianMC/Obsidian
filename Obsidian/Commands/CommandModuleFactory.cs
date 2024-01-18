using Microsoft.Extensions.DependencyInjection;
using Obsidian.Commands.Framework;
using Obsidian.Plugins;
using System.Reflection;

namespace Obsidian.Commands;
public static class CommandModuleFactory
{
    public static object? CreateModule(ObjectFactory factory, CommandContext context, PluginContainer pluginContainer)
    {
        var module = factory.Invoke(pluginContainer.ServiceScope.ServiceProvider, null);
        var moduleType = module.GetType();

        var commandContextProperty = moduleType.GetProperties().FirstOrDefault(x => x.GetCustomAttribute<CommandContextAttribute>() != null) 
            ?? throw new InvalidOperationException("Failed to find CommandContext property.");

        commandContextProperty.SetValue(module, context);

        pluginContainer.InjectServices(null, module);

        return module;
    }
}
