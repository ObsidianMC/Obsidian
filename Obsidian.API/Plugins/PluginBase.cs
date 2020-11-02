using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Provides the base class for a plugin.
    /// </summary>
    public abstract class PluginBase
    {
        private Type typeCache;

        /// <summary>
        /// Invokes a method in the class. For repeated calls use <see cref="GetMethod{T}(string, Type[])">GetMethod</see> or make a plugin wrapper.
        /// </summary>
        public object Invoke(string methodName, params object[] args)
        {
            var method = GetMethod(methodName, args);
            return method.Invoke(this, args);
        }

        /// <summary>
        /// Invokes a method in the class. For repeated calls use <see cref="GetMethod{T}(string, Type[])">GetMethod</see> or make a plugin wrapper.
        /// This method can be used on non-async methods too.
        /// </summary>
        public Task InvokeAsync(string methodName, params object[] args)
        {
            var method = GetMethod(methodName, args);
            var result = method.Invoke(this, args);
            if (result is Task task)
                return task;
            return Task.FromResult(result);
        }

        /// <summary>
        /// Invokes a method in the class. For repeated calls use <see cref="GetMethod{T}(string, Type[])">GetMethod</see> or make a plugin wrapper.
        /// </summary>
        public T Invoke<T>(string methodName, params object[] args)
        {
            var method = GetMethod(methodName, args);
            return (T)method.Invoke(this, args);
        }

        /// <summary>
        /// Invokes a method in the class. For repeated calls use <see cref="GetMethod{T}(string, Type[])">GetMethod</see> or make a plugin wrapper.
        /// This method can be used on non-async methods too.
        /// </summary>
        public Task<T> InvokeAsync<T>(string methodName, params object[] args)
        {
            var method = GetMethod(methodName, args);
            var result = method.Invoke(this, args);
            if (result is Task<T> task)
                return task;
            return Task.FromResult((T)result);
        }

        /// <summary>
        /// Returns a delegate for this plugin's method.
        /// </summary>
        public T GetMethod<T>(string methodName, params Type[] parameterTypes) where T : Delegate
        {
            return (T)GetMethod(methodName, parameterTypes).CreateDelegate(typeof(T), this);
        }

        /// <summary>
        /// Returns a delegate for this plugin's property getter.
        /// </summary>
        public Func<T> GetPropertyGetter<T>(string propertyName)
        {
            return GetMethod<Func<T>>($"get_{propertyName}");
        }

        /// <summary>
        /// Returns a delegate for this plugin's property setter.
        /// </summary>
        public Action<T> GetPropertySetter<T>(string propertyName)
        {
            return GetMethod<Action<T>>($"set_{propertyName}");
        }

        private MethodInfo GetMethod(string methodName, object[] args)
        {
            args ??= new object[0];
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                types[i] = args[i].GetType();
            }

            return GetMethod(methodName, types);
        }

        private MethodInfo GetMethod(string methodName, Type[] parameterTypes)
        {
            typeCache ??= GetType();
            var method = typeCache.GetMethod(methodName, parameterTypes);
            if (method == null)
            {
                throw new MissingMethodException(typeCache.Name, methodName);
            }
            return method;
        }
    }
}
