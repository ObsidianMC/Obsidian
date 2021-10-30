using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Provides the base class for a plugin wrapper.
    /// </summary>
    public abstract class PluginWrapper
    {
        internal PluginBase Plugin;

        public PluginWrapper(PluginBase plugin)
        {
            Plugin = plugin;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public PluginWrapper()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {

        }

        /// <summary>
        /// Invokes a method in the class. For repeated calls use <see cref="GetMethod{T}(string, Type[])">GetMethod</see> or make a plugin wrapper.
        /// </summary>
        public object? Invoke(string methodName, params object[] args)
        {
            return Plugin.Invoke(methodName, args);
        }

        /// <summary>
        /// Invokes a method in the class. For repeated calls use <see cref="GetMethod{T}(string, Type[])">GetMethod</see> or make a plugin wrapper.
        /// </summary>
        public T? Invoke<T>(string methodName, params object[] args)
        {
            return Plugin.Invoke<T>(methodName, args);
        }

        /// <summary>
        /// Returns a delegate for this plugin's method.
        /// </summary>
        public T GetMethod<T>(string methodName, params Type[] parameterTypes) where T : Delegate
        {
            return Plugin.GetMethod<T>(methodName, parameterTypes);
        }

        /// <summary>
        /// Returns a delegate for this plugin's property getter.
        /// </summary>
        public Func<T> GetPropertyGetter<T>(string propertyName)
        {
            return Plugin.GetPropertyGetter<T>(propertyName);
        }

        /// <summary>
        /// Returns a delegate for this plugin's property setter.
        /// </summary>
        public Action<T> GetPropertySetter<T>(string propertyName)
        {
            return Plugin.GetPropertySetter<T>(propertyName);
        }
    }
}
