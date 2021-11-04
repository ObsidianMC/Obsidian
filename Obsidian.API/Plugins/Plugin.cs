//     Obsidian.API/Plugin.cs
//     Copyright (C) 2021

using Obsidian.API.Plugins.Services;
using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Base class for all plugins
    /// </summary>
    public abstract class Plugin : IEquatable<Plugin>
    {
        /// <summary>
        /// Internal globally unique identifier for the plugin
        /// <example><code>org.example.SamplePlugin</code></example>
        /// </summary>
        public abstract string Identifier { get; }

        /// <summary>
        /// List of dependencies of the plugin. The plugin won't be loaded if any of those it not met.
        /// </summary>
        // ReSharper disable once ReturnTypeCanBeEnumerable.Global
        public virtual PluginDependency[] Dependencies { get; } = Array.Empty<PluginDependency>();
        
        /// <summary>
        /// Information about the plugin
        /// </summary>
        public abstract PluginInfo Info { get; }
        
        /// <summary>
        /// Called when initializing the plugins
        /// </summary>
        /// <remarks><b>Note: </b>Can also be called after reloading plugin while the server is running</remarks>
        /// <param name="serviceProvider">Source of services provided by other plugins and sources</param>
        public virtual void Initialize(IPluginServiceProvider serviceProvider) { }
        
        /// <summary>
        /// Called when de-initializing the plugin
        /// </summary>
        /// <remarks><b>Note: </b>Can also be called when reloading plugin while the server is running</remarks>
        public virtual void DeInitialize() { }


        /// <inheritdoc />
        public bool Equals(Plugin? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Identifier == other.Identifier && Info.Equals(other.Info);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((Plugin) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Identifier, Info);
        
        /// <summary>
        /// Tests the equality of plugins
        /// </summary>
        /// <param name="left">Left plugin</param>
        /// <param name="right">Right plugin</param>
        /// <returns>If plugins are equal</returns>
        public static bool operator ==(Plugin? left, Plugin? right) => Equals(left, right);

        /// <summary>
        /// Tests the inequality of plugins
        /// </summary>
        /// <param name="left">Left plugin</param>
        /// <param name="right">Right plugin</param>
        /// <returns>If plugins are not equal</returns>
        public static bool operator !=(Plugin? left, Plugin? right) => !Equals(left, right);
    }
}