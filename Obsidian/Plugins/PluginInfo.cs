using JetBrains.Annotations;
using Newtonsoft.Json;
using System;
using System.Linq;

namespace Obsidian.Plugins
{
    public class PluginInfo
    {
        [JsonProperty]
        public string Name { get; internal set; }

        [JsonProperty]
        public string[] Authors { get; internal set; } = Array.Empty<string>();

        [JsonProperty]
        public string Version { get; internal set; }

        [JsonProperty]
        public string Description { get; internal set; }


        [CanBeNull]
        [JsonProperty]
        public string ProjectUrl { get; internal set; }

        public PluginInfo() { }

        public PluginInfo SetName(string name)
        {
            this.Name = name;

            return this;
        }

        public PluginInfo AddAuthor(string author)
        {
            this.Authors.Append(author);

            return this;
        }

        public PluginInfo SetVersion(string version)
        {
            this.Version = version;
            return this;
        }

        public PluginInfo SetDescription(string description)
        {
            this.Description = description;

            return this;
        }

        public PluginInfo SetProjectUrl(string url)
        {
            this.ProjectUrl = url;
            return this;
        }
    }
}