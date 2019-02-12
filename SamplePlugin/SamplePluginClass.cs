
using System;
using System.Threading.Tasks;
using Obsidian;
using Obsidian.Commands;
using Obsidian.Events.EventArgs;
using Obsidian.Plugins;
using Qmmands;

namespace SamplePlugin
{
    public class SamplePluginClass : IPluginClass
    {
        Server server;

        public PluginInfo Initialize(Server server)
        {
            this.server = server;

            server.Commands.AddModule<SamplePluginCommands>();

            server.Events.PlayerJoin += OnPlayerJoin;

            return new PluginInfo(
                "SamplePlugin",
                "Obsidian Team",
                "0.1",
                "A Sample Plugin! <3",
                "https://github.com/NaamloosDT/Obsidian"
            );
        }

        private async Task OnPlayerJoin(PlayerJoinEventArgs e)
        {
            await e.Server.SendChatAsync($"Player join event from sample plugin! {e.Player.Username}", e.Client, 0, true);
            await e.Logger.LogMessageAsync($"Player join event to logger from sample plugin! {e.Player.Username}");
        }
    }

    public class SamplePluginCommands : ModuleBase<CommandContext>
    {
        public CommandService Service { get; set; }

        [Command("samplecommand")]
        [Description("A sample command added by a sample plugin!")]
        public async Task SampleCommandAsync()
        {
            await Context.Server.SendChatAsync($"Sample command executed by {Context.Player.Username}" +
                $" from within a sample plugin!!!1", Context.Client, 0, false);
        }
    }
}
