using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Provides information about the plugin.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class PluginAttribute : Attribute
    {
        /// <summary>
        /// Plugin attribute constructor.
        /// </summary>
        /// <param name="name"></param>
        public PluginAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Name of the plugin.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Version of the plugin. The string should contain the major, minor, <i>[build]</i>, and <i>[revision]</i> numbers, split by a period character ('.').
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Name(s) of the plugin's author(s).
        /// </summary>
        public string Authors { get; set; }

        /// <summary>
        /// Description of the plugin.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// URL address of where the plugin is hosted.
        /// </summary>
        public string ProjectUrl { get; set; }
    }
}
