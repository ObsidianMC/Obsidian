using JetBrains.Annotations;

namespace Obsidian.Plugins
{
    public class PluginInfo
    {
        public string Name { get; }
        public string[] Authors { get; }
        public string Version { get; }
        public string Description { get; }
        
        [CanBeNull]
        public string ProjectUrl { get; }

        public PluginInfo(string name, string author, string version, string description, string projectUrl = null)
            : this(name, new[] {author}, version, description, projectUrl)
        {
        }
        
        public PluginInfo(string name, string[] authors, string version, string description, string projectUrl = null)
        {
            this.Name = name;
            this.Authors = authors;
            this.Version = version;
            this.Description = description;
            this.ProjectUrl = projectUrl;
        }
    }
}