using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.Reflection;

namespace Obsidian.Plugins.ServiceProviders;

public static class PluginServiceHandler
{
    public static void InjectServices(IServiceProvider serviceProvider, PluginContainer container, ILogger logger) => InjectServices(serviceProvider, container.Plugin, container, logger);

    public static void InjectServices(IServiceProvider serviceProvider, object o, PluginContainer container, ILogger logger)
    {
        var loggerProvider = serviceProvider.GetRequiredService<ILoggerProvider>();

        PropertyInfo[] properties = o.GetType().GetProperties();
        foreach (var property in properties)
        {
            if (property.GetCustomAttribute<InjectAttribute>() == null)
                continue;

            if (property.PropertyType.IsAssignableTo(typeof(PluginBase)))
                continue;

            try
            {
                object service = default!;
                if (property.PropertyType == typeof(ILogger))
                    service = loggerProvider.CreateLogger(container.Info.Name);
                else
                    service = serviceProvider.GetRequiredService(property.PropertyType);


                property.SetValue(o, service);
            }
            catch
            {
                logger?.LogError("Failed injecting into {pluginName}.{propertyName} property, because {propertyType} is not a valid service.", container.Info.Name,
                   property.Name, property.PropertyType);
            }
        }
    }
}
