using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Plugins
{
    // Plugins derive from this base class.
    public interface IPluginClass
    {
        /// <summary>
        /// Initializes a plugin, and returns a new PluginInfo object.
        /// </summary>
        /// <param name="server">Server to intialize plugin in</param>
        /// <returns></returns>
        PluginInfo Initialize(Server server);
    }

    public class PluginInfo
    {
        public string Name { get; private set; }
        public string Author { get; private set; }
        public string Version { get; private set; }
        public string Description { get; private set; }
        public string ProjectUrl { get; private set; }

        public PluginInfo(string name, string author, string version, string description, string projecturl)
        {
            this.Name = name;
            this.Author = author;
            this.Version = version;
            this.Description = description;
            this.ProjectUrl = projecturl;
        }
    }
}
