using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.Reflection;

namespace Obsidian.Plugins.ServiceProviders;

public static class PluginServiceHandler
{
    public static void InjectServices(IServiceProvider provider, PluginContainer container, ILogger logger) =>
        InjectServices(provider, container.Plugin, container, logger);

    public static void InjectServices(IServiceProvider provider, object target, PluginContainer container, ILogger logger)
    {
        PropertyInfo[] properties = target.GetType().GetProperties();
        foreach (var property in properties)
            InjectService(provider, property, target, logger, container.Info.Name);
    }

    private static void InjectService(IServiceProvider provider, PropertyInfo property, object target, ILogger logger, string pluginName)
    {
        if (property.GetCustomAttribute<InjectAttribute>() == null)
            return;

        if (property.PropertyType.IsAssignableTo(typeof(PluginBase)))
            return;

        if (property.GetValue(target) != null)
            return;

        try
        {
            object service = default!;

            if (property.PropertyType == typeof(ILogger))
            {
                var loggerProvider = provider.GetRequiredService<ILoggerProvider>();

                service = loggerProvider.CreateLogger(pluginName);
            }
            else
                service = provider.GetRequiredService(property.PropertyType);

            property.SetValue(target, service);
        }
        catch
        {
            logger?.LogError("Failed injecting into {pluginName}.{propertyName} property, because {propertyType} is not a valid service.", pluginName,
               property.Name, property.PropertyType);
        }
    }
}
