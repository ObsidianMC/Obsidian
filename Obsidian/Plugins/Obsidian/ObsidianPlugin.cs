using System.Threading.Tasks;

namespace Obsidian.Plugins.Obsidian
{
    public class ObsidianPlugin : Plugin
    {
        private ObsidianPluginClass Class { get; }

        public ObsidianPlugin(PluginSource source, string path, ObsidianPluginClass @class) : base(source, path, @class.Info)
        {
            this.Class = @class;
        }

        public override async Task LoadAsync(Server server)
        {
            this.Class.Server = server;
            await this.Class.InitializeAsync();
        }
    }
}