using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.Reflection;

namespace Obsidian.Plugins.ServiceProviders;

public static class PluginServiceHandler
{
    private static readonly Type pluginBaseType = typeof(PluginBase);

    public static void InjectServices(IServiceProvider provider, PluginContainer container, ILogger logger) =>
        InjectServices(provider, container.Plugin, container, logger);

    public static void InjectServices(IServiceProvider provider, object target, PluginContainer container, ILogger logger)
    {
        var properties = target.GetType()
            .GetProperties()
            .Where(x => x.GetCustomAttribute<InjectAttribute>() != null && !x.PropertyType.IsAssignableTo(pluginBaseType));

        foreach (var property in properties)
            InjectService(provider, new() { Property = property, Target = target }, container.Info.Name, logger);
    }

    private static void InjectService(IServiceProvider provider, Injectable injectable, string pluginName, ILogger logger)
    {
        var property = injectable.Property;
        var target = injectable.Target;

        if (property.GetValue(injectable.Target) != null)
            return;

        try
        {
            object service = provider.GetRequiredService(property.PropertyType);

            property.SetValue(target, service);
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Failed to inject service.");
        }
    }
}

public readonly struct Injectable
{
    public required PropertyInfo Property { get; init; }

    public required object Target { get; init; }
}
