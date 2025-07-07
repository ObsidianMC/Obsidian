using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Obsidian.Plugins.ServiceProviders;

public static class PluginServiceHandler
{
    public static void InjectServices(IServiceProvider provider, PluginContainer container, ILogger logger) =>
        InjectServices(provider, container.Plugin, logger);

    public static void InjectServices(IServiceProvider provider, object target,  ILogger logger)
    {
        var properties = target.GetType().WithInjectAttribute();

        foreach (var property in properties)
            InjectService(provider, property, target, logger);
    }

    private static void InjectService(IServiceProvider provider, PropertyInfo property, object target, ILogger logger)
    {
        if (property.GetValue(target) != null)
            return;

        try
        {
            object service = provider.GetRequiredService(property.PropertyType);

            property.SetValue(target, service);
        }
        catch(Exception ex)
        {
            logger.LogWarning(ex, "Failed to inject service into plugin property.");//Not as important
        }
    }
}
