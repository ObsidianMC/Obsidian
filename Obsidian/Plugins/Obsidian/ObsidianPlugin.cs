using System.Threading.Tasks;

namespace Obsidian.Plugins.Obsidian
{
    public class ObsidianPlugin : Plugin
    {
        private IObsidianPluginClass Class { get; }

        public ObsidianPlugin(PluginSource source, string path, IObsidianPluginClass @class) : base(source, path, @class.Info)
        {
            this.Class = @class;
        }

        public override void Load(Server server) => LoadAsync(server).GetAwaiter().GetResult();

        public override async Task LoadAsync(Server server) => await Class.InitializeAsync(server);
    }
}