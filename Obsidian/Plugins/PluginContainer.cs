﻿using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using System.Reflection;
using System.Runtime.Loader;

namespace Obsidian.Plugins;

public sealed class PluginContainer : IDisposable
{
    public PluginBase Plugin { get; private set; }
    public PluginInfo Info { get; }
    public string Source { get; internal set; }
    public AssemblyLoadContext LoadContext { get; private set; }
    public string ClassName { get; }

    public bool HasDependencies { get; private set; } = true;
    public bool IsReady => HasDependencies;
    public bool Loaded { get; internal set; }

    internal List<IDisposable> DisposableServices { get; } = new();
    internal List<ScheduledDependencyInjection> ScheduledDependencyInjections { get; } = new();
    internal Dictionary<EventContainer, Delegate> EventHandlers { get; } = new();

    private Type pluginType;

    public PluginContainer(PluginInfo info, string source)
    {
        Info = info;
        Source = source;
    }

    public PluginContainer(PluginBase plugin, PluginInfo info, Assembly assembly, AssemblyLoadContext loadContext, string source)
    {
        Plugin = plugin;
        Info = info;
        LoadContext = loadContext;
        Source = source;

        pluginType = plugin.GetType();
        ClassName = pluginType.Name;

        Plugin.Info = Info;
    }

    internal void RegisterDisposableService(IDisposable disposableService)
    {
        DisposableServices.Add(disposableService);
    }

    #region Dependencies
    public void RegisterDependencies(PluginManager manager, ILogger logger = null)
    {
        // FieldInfo[] and PropertyInfo[] can't be merged into MemberInfo[], since it includes methods etc. and MemberInfo doesn't have SetValue method

        FieldInfo[] fields = pluginType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            var attribute = field.GetCustomAttribute<DependencyAttribute>();
            if (attribute != null)
            {
                if (field.FieldType != typeof(PluginBase) && !field.FieldType.IsSubclassOf(typeof(PluginWrapper)))
                {
                    logger?.LogWarning($"Failed injecting into {Info.Name}.{field.Name} property, because it's not PluginBase or PluginWrapper.");
                    continue;
                }

                string name = field.GetCustomAttribute<AliasAttribute>()?.Identifier ?? field.Name;
                PluginBase dependency = GetDependency(manager, logger, name, attribute.GetMinVersion())?.Plugin;

                if (dependency != null)
                {
                    field.SetValue(Plugin, CreateInjection(field.FieldType, dependency, logger));
                }
                else
                {
                    ScheduledDependencyInjections.Add(new ScheduledDependencyInjection(name,
                                                                                       attribute,
                                                                                       injection: plugin => field.SetValue(Plugin, CreateInjection(field.FieldType, plugin, logger))));
                    if (!attribute.Optional)
                        HasDependencies = false;
                }
            }
        }

        PropertyInfo[] properties = pluginType.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            var attribute = property.GetCustomAttribute<DependencyAttribute>();
            if (attribute != null)
            {
                if (property.PropertyType != typeof(PluginBase) && !property.PropertyType.IsSubclassOf(typeof(PluginWrapper)))
                {
                    logger?.LogWarning($"Failed injecting into {Info.Name}.{property.Name} property, because it's not PluginBase or PluginWrapper.");
                    continue;
                }

                string name = property.GetCustomAttribute<AliasAttribute>()?.Identifier ?? property.Name;
                PluginBase dependency = GetDependency(manager, logger, name, attribute.GetMinVersion())?.Plugin;

                if (dependency != null)
                {
                    property.SetValue(Plugin, CreateInjection(property.PropertyType, dependency, logger));
                }
                else
                {
                    ScheduledDependencyInjections.Add(new ScheduledDependencyInjection(name,
                                                                                       attribute,
                                                                                       injection: plugin => property.SetValue(Plugin, CreateInjection(property.PropertyType, plugin, logger))));
                    if (!attribute.Optional)
                        HasDependencies = false;
                }
            }
        }
    }

    private static object CreateInjection(Type targetType, PluginBase plugin, ILogger logger = null)
    {
        if (targetType == typeof(PluginBase))
        {
            return plugin;
        }
        else
        {
            PluginWrapper wrapper;
            try
            {
                wrapper = (PluginWrapper)Activator.CreateInstance(targetType);
            }
            catch
            {
                logger?.LogWarning($"Failed while creating '{targetType.Name}', because it doesn't have accesible parameterless constructor.");
                return null;
            }

            wrapper.plugin = plugin;

            Type pluginType = plugin.GetType();
            var methods = pluginType.GetMethods();
            foreach (var property in targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(p => p.PropertyType.IsSubclassOf(typeof(Delegate))))
            {
                string methodName = property.GetCustomAttribute<AliasAttribute>()?.Identifier ?? property.Name;
                var delegateSignature = property.PropertyType.GetMethod("Invoke");
                var returnType = delegateSignature.ReturnType;
                var parameterTypes = delegateSignature.GetParameters().Select(p => p.ParameterType);
                var method = methods.Where(m => m.Name == methodName).FirstOrDefault(m => m.ReturnType == returnType && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes));
                if (method == null)
                {
                    logger?.LogWarning($"Couldn't inject into {targetType.Name}.{property.Name}, because no method with matching name was found.");
                    continue;
                }

                Delegate @delegate;
                try
                {
                    @delegate = method.CreateDelegate(property.PropertyType, plugin);
                }
                catch
                {
                    logger?.LogWarning($"Couldn't inject into {targetType.Name}.{property.Name}, because no method with matching signature was found.");
                    continue;
                }
                property.SetValue(wrapper, @delegate);
            }

            return wrapper;
        }
    }

    private static PluginContainer GetDependency(PluginManager manager, ILogger logger, string name, Version minVersion)
    {
        foreach (var plugin in manager.Plugins)
        {
            if (IsValidDependency(name, minVersion, actual: plugin, logger))
                return plugin;
        }

        return null;
    }

    public bool TryAddDependency(PluginContainer plugin, ILogger logger)
    {
        for (int i = 0; i < ScheduledDependencyInjections.Count; i++)
        {
            ScheduledDependencyInjection scheduledDependency = ScheduledDependencyInjections[i];
            if (IsValidDependency(scheduledDependency.Name, scheduledDependency.Info.GetMinVersion(), actual: plugin, logger))
            {
                scheduledDependency.Inject(plugin.Plugin);
                ScheduledDependencyInjections.RemoveAt(i--);
            }
        }

        if (!HasDependencies)
        {
            foreach (var scheduledDependency in ScheduledDependencyInjections)
            {
                if (!scheduledDependency.Info.Optional)
                    return false;
            }
        }
        return HasDependencies = true;
    }

    private static bool IsValidDependency(string expectedName, Version minVersion, PluginContainer actual, ILogger logger)
    {
        if (actual.Info.Name == expectedName || actual.ClassName == expectedName)
        {
            if (minVersion != null && actual.Info.Version != null && actual.Info.Version < minVersion)
            {
                logger?.LogWarning($"Found matching dependency '{actual.Info.Name}', but with older version {actual.Info.Version} (minimum: {minVersion})");
                return false;
            }
            return true;
        }

        return false;
    }
    #endregion

    public void Dispose()
    {
        Plugin = null;
        LoadContext = null;
        pluginType = null;
        ScheduledDependencyInjections.Clear();
        EventHandlers.Clear();
    }
}
