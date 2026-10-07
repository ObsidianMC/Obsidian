using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Obsidian.Plugins.ServiceProviders;

public static partial class PluginServiceHandler
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
        if (property.GetValue(target) is not null)
            return;

        try
        {
            object service = provider.GetRequiredService(property.PropertyType);

            property.SetValue(target, service);
        }
        catch(Exception ex)
        {
            Log.InjectionFailed(logger, ex, property.PropertyType.Name, property.Name);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to inject {ServiceType} into plugin property {PropertyName}")]
        public static partial void InjectionFailed(ILogger logger, Exception exception, string serviceType, string propertyName);
    }
}
