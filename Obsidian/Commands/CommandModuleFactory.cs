using Microsoft.Extensions.DependencyInjection;
using Obsidian.Plugins;
using System.Diagnostics;
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

    public static object CreateModule(ObjectFactory factory, CommandContext context, IServiceProvider serviceProvider)
    {
        var module = factory.Invoke(serviceProvider, null);
        var moduleType = module.GetType();

        var commandContextProperty = moduleType.GetProperties().FirstOrDefault(x => x.GetCustomAttribute<CommandContextAttribute>() != null) ?? 
            throw new UnreachableException();//This should never happen

        commandContextProperty.SetValue(module, context);

        return module;
    }
}
