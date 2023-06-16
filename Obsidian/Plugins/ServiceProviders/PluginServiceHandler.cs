using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.Reflection;

namespace Obsidian.Plugins.ServiceProviders;

public static class PluginServiceHandler
{
    public static void InjectServices(IServiceProvider provider, PluginContainer container, ILogger logger, ILoggerProvider loggerProvider) =>
        InjectServices(provider, container.Plugin, container, logger, loggerProvider);

    public static void InjectServices(IServiceProvider provider, object target, PluginContainer container, ILogger logger, ILoggerProvider loggerProvider)
    {
        PropertyInfo[] properties = target.GetType().GetProperties();
        foreach (var property in properties)
            InjectService(provider, new() { Property = property, Target = target }, container.Info.Name, logger, loggerProvider);
    }

    private static void InjectService(IServiceProvider provider, Injectable injectable, string pluginName, ILogger logger, ILoggerProvider loggerProvider)
    {
        var property = injectable.Property;
        var target = injectable.Target;

        if (property.GetCustomAttribute<InjectAttribute>() == null)
            return;

        if (property.PropertyType.IsAssignableTo(typeof(PluginBase)))
            return;

        if (property.GetValue(injectable.Target) != null)
            return;

        try
        {
            object service = default!;

            if (property.PropertyType == typeof(ILogger))
                service = loggerProvider.CreateLogger(pluginName);
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

public readonly struct Injectable
{
    public required PropertyInfo Property { get; init; }

    public required object Target { get; init; }
}
