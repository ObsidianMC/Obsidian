using Obsidian;
using Obsidian.Commands;
using Obsidian.Events.EventArgs;
using Obsidian.Plugins;

using Qmmands;

using System.Threading.Tasks;
using Obsidian.Plugins.Obsidian;

namespace SamplePlugin
{
    public class SamplePluginClass : IObsidianPluginClass
    {
        private Server server;

        public PluginInfo Info => new PluginInfo(
            "SamplePlugin",
            "Obsidian Team",
            "0.1",
            "A Sample Plugin! <3",
            "https://github.com/NaamloosDT/Obsidian"
        );
        
        public async Task InitializeAsync(Server server)
        {
            this.server = server;

            server.Commands.AddModule<SamplePluginCommands>();

            server.Events.PlayerJoin += OnPlayerJoin;

            server.Register(new DickWorldGenerator());
        }
        
        private async Task OnPlayerJoin(PlayerJoinEventArgs e)
        {
            e.Server.Broadcast($"Player join event from sample plugin! {e.Joined.Username}");
            await e.Logger.LogMessageAsync($"Player join event to logger from sample plugin! {e.Joined.Username}");
        }
    }

    public class SamplePluginCommands : ModuleBase<CommandContext>
    {
        public CommandService Service { get; set; }

        [Command("samplecommand")]
        [Description("A sample command added by a sample plugin!")]
        public Task SampleCommandAsync()
        {
            Context.Server.Broadcast($"Sample command executed by {Context.Player.Username} from within a sample plugin!!!");

            return Task.CompletedTask;
        }
    }
}