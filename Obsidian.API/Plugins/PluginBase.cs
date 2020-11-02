using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Invokes a method in the class. The actual method can accept less parameters than <c>args</c>.
        /// If exception occurs, it is returned inside <see cref="AggregateException"/>.
        /// </summary>
        public object FriendlyInvoke(string methodName, params object[] args)
        {
            var (method, parameterCount) = GetFriendlyMethod(methodName, args);
            if (parameterCount != args.Length)
                args = args.Take(parameterCount).ToArray();
            try
            {
                return method.Invoke(this, args);
            }
            catch (Exception e)
            {
                return new AggregateException(e);
            }
        }

        /// <summary>
        /// Invokes a method in the class. The actual method can accept less parameters than <c>args</c>.
        /// If exception occurs, it is returned inside <see cref="AggregateException"/>.
        /// This method can be used on non-async methods too.
        /// </summary>
        public Task FriendlyInvokeAsync(string methodName, params object[] args)
        {
            var (method, parameterCount) = GetFriendlyMethod(methodName, args);
            if (parameterCount != args.Length)
                args = args.Take(parameterCount).ToArray();
            try
            {
                var result = method.Invoke(this, args);
                if (result is Task task)
                    return task;
                return Task.FromResult(result);
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
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
            return method;
        }

        private (MethodInfo method, int parameterCount) GetFriendlyMethod(string methodName, object[] args)
        {
            typeCache ??= GetType();
            args ??= new object[0];
            IEnumerable<(MethodInfo method, ParameterInfo[] parameters)> methods = typeCache
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.Name == methodName)
                .Select(method => (method, parameters: method.GetParameters()))
                .Where(m => m.parameters.Length <= args.Length)
                .OrderByDescending(m => m.parameters.Length);

            if (methods.Count() == 0)
                return (null, -1);

            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                types[i] = args[i].GetType();
            }

            foreach (var (method, parameters) in methods)
            {
                bool match = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (!parameters[i].ParameterType.IsAssignableFrom(types[i]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return (method, parameters.Length);
            }

            return (null, -1);
        }
    }
}
