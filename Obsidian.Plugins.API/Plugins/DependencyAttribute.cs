using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Indicates that a field/property should be set to an instance of a plugin with the same name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class DependencyAttribute : Attribute
    {
        /// <summary>
        /// Indicates that the plugin can run without this dependency.
        /// </summary>
        public bool Optional { get; set; }

        /// <summary>
        /// Minimal version of the dependency that can be used. The string should contain the major, minor, <i>[build]</i>, and <i>[revision]</i> numbers, split by a period character ('.').
        /// </summary>
        public string MinVersion { get; set; }

        /// <summary>
        /// Name of the dependency that should be injected. Keep as null to use property name.
        /// </summary>
        public string Alias { get; set; }
    }
}
