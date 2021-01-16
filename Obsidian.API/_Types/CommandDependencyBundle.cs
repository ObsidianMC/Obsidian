using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API
{
    /// <summary>
    /// Represents a bundle of dependencies for a plugin's commands.
    /// </summary>
    public class CommandDependencyBundle
    {
        private List<object> depencencies;
        private object _lock;

        public CommandDependencyBundle()
        {
            this.depencencies = new List<object>();
        }

        public bool HasType(Type t)
        {
            return this.depencencies.Any(x => x.GetType() == t);
        }

        /// <summary>
        /// Registers a new dependency.
        /// </summary>
        /// <typeparam name="T">Type of dependency to register.</typeparam>
        /// <param name="dependency">Dependency to register.</param>
        /// <returns></returns>
        public virtual async Task RegisterDependencyAsync<T>(T dependency)
        {
            // We'll want this async as it has a lock.
            await Task.Yield();

            lock (_lock)
            {
                if (depencencies.Any(x => x.GetType() == typeof(T)))
                {
                    throw new Exception($"A dependency with type {typeof(T)} was already registered in this bundle.");
                }

                depencencies.Add(dependency);
            }
        }

        /// <summary>
        /// Gets a registered dependency.
        /// </summary>
        /// <typeparam name="T">Type of dependency to return.</typeparam>
        /// <returns>The requested dependency.</returns>
        public virtual async Task<T> GetDependencyAsync<T>()
        {
            // We'll want this async as it has a lock.
            await Task.Yield();

            lock (_lock)
            {
                if (!depencencies.Any(x => x.GetType() == typeof(T)))
                {
                    throw new Exception($"No dependency with type {typeof(T)} was registered in this bundle.");
                }

                return (T)depencencies.First(x => x.GetType() == typeof(T));
            }
        }

        /// <summary>
        /// Gets a registered dependency.
        /// </summary>
        /// <typeparam name="T">Type of dependency to return.</typeparam>
        /// <returns>The requested dependency.</returns>
        public virtual async Task<object> GetDependencyAsync(Type t)
        {
            // We'll want this async as it has a lock.
            await Task.Yield();

            lock (_lock)
            {
                if (!depencencies.Any(x => x.GetType() == t))
                {
                    throw new Exception($"No dependency with type {t} was registered in this bundle.");
                }

                return depencencies.First(x => x.GetType() == t);
            }
        }
    }

    internal class NullDependency : CommandDependencyBundle
    {
        public override Task<T> GetDependencyAsync<T>()
        {
            throw new NullReferenceException("There were no dependencies registered for this plugin!");
        }

        public override Task RegisterDependencyAsync<T>(T dependency)
        {
            throw new NullReferenceException("There were no dependencies registered for this plugin!");
        }
    }
}
