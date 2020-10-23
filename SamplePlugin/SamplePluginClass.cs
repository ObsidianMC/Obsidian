using Microsoft.Extensions.Logging;
using Obsidian.Commands;
using Obsidian.Events.EventArgs;
using Obsidian.API;
using Obsidian.API.Obsidian;
using Qmmands;
using System.Threading.Tasks;

namespace SamplePlugin
{
    public class SamplePluginClass : ObsidianPluginClass
    {
        public ILogger Logger { get; private set; }

        public override async Task InitializeAsync()
        {
            this.Logger = this.Server.LoggerProvider.CreateLogger("SamplePlugin");

            this.Info = new PluginInfo().SetName("SamplePlugin")
                .AddAuthor("Obsidian Team")
                .SetVersion("0.1")
                .SetDescription("A sample plugin! <3");

            this.Server.Commands.AddModule<SamplePluginCommands>();

            this.Server.Events.PlayerJoin += OnPlayerJoin;

            await this.Server.RegisterAsync(new DickWorldGenerator());
        }

        private async Task OnPlayerJoin(PlayerJoinEventArgs e)
        {
            await e.Server.BroadcastAsync($"Player join event from sample plugin! {e.Player.Username}");
            this.Logger.LogInformation($"Player join event to logger from sample plugin! {e.Player.Username}");
        }
    }

    public class SamplePluginCommands : ModuleBase<ObsidianContext>
    {
        public CommandService Service { get; set; }

        [Command("samplecommand")]
        [Description("A sample command added by a sample plugin!")]
        public async Task SampleCommandAsync()
        {
            
            await this.Context.Server.BroadcastAsync($"Sample command executed by {Context.Player.Username} from within a sample plugin!!!");
        }
    }
}