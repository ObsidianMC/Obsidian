using Obsidian.API.Plugins;
using System;

namespace Obsidian.Plugins
{
    public class PluginInfo : IPluginInfo
    {
        public string Name { get; }
        public Version Version { get; }
        public string Description { get; }
        public string Authors { get; }
        public Uri ProjectUrl { get; }

        internal PluginInfo(string name)
        {
            Name = name;
            Version = new Version();
            Description = string.Empty;
            Authors = "Unknown";
        }

        internal PluginInfo(string name, PluginAttribute attribute)
        {
            Name = attribute.Name ?? name;
            Description = attribute.Description ?? string.Empty;
            Authors = attribute.Authors ?? "Unknown";

            if (Version.TryParse(attribute.Version, out var version))
                Version = version;
            else
                Version = new Version(1, 0);

            if (Uri.TryCreate(attribute.ProjectUrl, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
                ProjectUrl = url;
        }
    }
}
