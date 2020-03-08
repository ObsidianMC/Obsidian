using System.Threading.Tasks;

namespace Obsidian.Plugins
{
    public abstract class Plugin
    {
        public PluginSource Source { get; }
        public PluginInfo Info { get; }

        /// <summary>
        /// The file path of this plugin
        /// </summary>
        /// <example>
        /// <list type="bullet">
        ///     <item>
        ///         <term>Obsidian plugins</term>
        ///         <description>.../plugins/ObsidianPlugin.dll</description>
        ///     </item>
        ///     <item>
        ///         <term>Bukkit/Spigot/Java plugins</term>
        ///         <description>.../plugins/WorldEdit.jar</description>
        ///     </item>
        /// </list>
        /// </example>
        public string Path { get; }
        
        public Plugin(PluginSource source, string path, PluginInfo info)
        {
            this.Source = source;
            this.Path = path;
            this.Info = info;
        }

        public abstract void Load(Server server);

        public abstract Task LoadAsync(Server server);
    }
}