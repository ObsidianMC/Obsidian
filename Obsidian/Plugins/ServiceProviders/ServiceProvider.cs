using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Obsidian.Plugins.ServiceProviders
{
    public class ServiceProvider
    {
        private static Dictionary<Type, Type> serviceImplementationTypes;
        private static Dictionary<Type, SingletonServiceBase> singletons;

        private const string serviceImplementationsSource = "Obsidian.Plugins.Services";
        private const string serviceModelsSource = "Obsidian.API.Plugins.Services";

        private ServiceProvider()
        {
        }

        public void InjectServices(PluginContainer container, ILogger logger) => InjectServices(container.Plugin, container, logger);
        
        public void InjectServices(object o, PluginContainer container, ILogger logger)
        {
            var serviceCache = new Dictionary<Type, object>();

            PropertyInfo[] properties = o.GetType().GetProperties();
            foreach (var property in properties)
            {
                if (property.GetCustomAttribute<InjectAttribute>() == null)
                    continue;

                if (property.PropertyType.IsAssignableTo(typeof(PluginBase)))
                    continue;

                if (serviceImplementationTypes.TryGetValue(property.PropertyType, out var implementationType))
                {
                    if (!serviceCache.TryGetValue(property.PropertyType, out var service))
                    {
                        service = Activator.CreateInstance(implementationType, container);
                        serviceCache.Add(property.PropertyType, service);

                        if (service is SecuredServiceBase securedService)
                            container.RegisterSecuredService(securedService);

                        if (service is IDisposable disposableService)
                            container.RegisterDisposableService(disposableService);
                    }

                    property.SetValue(o, service);
                }
                else if (singletons.TryGetValue(property.PropertyType, out var singleton))
                {
                    property.SetValue(o, singleton);
                }
                else
                {
                    logger?.LogError($"Failed injecting into {container.Info.Name}.{property.Name} property, because {property.PropertyType} is not a valid service.");
                }
            }
        }

        public static ServiceProvider Create()
        {
            if (serviceImplementationTypes == null)
                Initialize();

            return new ServiceProvider();
        }

        private static void Initialize()
        {
            serviceImplementationTypes = new Dictionary<Type, Type>();
            singletons = new Dictionary<Type, SingletonServiceBase>();

            var modelTypes = Assembly.GetAssembly(typeof(IService)).GetTypes().Where(type => type.Namespace == serviceModelsSource);
            var allTypes = Assembly.GetAssembly(typeof(ServiceProvider)).GetTypes();
            var implementationTypes = allTypes.Where(type => type.Namespace == serviceImplementationsSource);

            foreach (var model in modelTypes)
            {
                var implementation = implementationTypes.FirstOrDefault(type => model.IsAssignableFrom(type));
                if (implementation != null)
                {
                    if (typeof(SingletonServiceBase).IsAssignableFrom(implementation))
                    {
                        singletons.Add(implementation, Activator.CreateInstance(implementation) as SingletonServiceBase);
                    }
                    else
                    {
                        serviceImplementationTypes.Add(model, implementation);
                    }
                }
            }
        }
    }
}
