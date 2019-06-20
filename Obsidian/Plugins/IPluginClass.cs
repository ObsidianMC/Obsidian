namespace Obsidian.Plugins
{
    // Plugins derive from this base class.
    public interface IPluginClass
    {
        /// <summary>
        /// Initializes a plugin, and returns a new PluginInfo object.
        /// </summary>
        /// <param name="server">Server to intialize plugin in</param>
        /// <returns>The information about a plugin which will be represented in plugin listings</returns>
        PluginInfo Initialize(Server server);
    }

    public class PluginInfo
    {
        public string Name { get; }
        public string Author { get; }
        public string Version { get; }
        public string Description { get; }
        public string ProjectUrl { get; }

        public PluginInfo(string name, string author, string version, string description, string projectUrl)
        {
            this.Name = name;
            this.Author = author;
            this.Version = version;
            this.Description = description;
            this.ProjectUrl = projectUrl;
        }
    }
}