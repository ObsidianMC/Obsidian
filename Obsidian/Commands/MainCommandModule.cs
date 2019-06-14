using Obsidian.Boss;
using Obsidian.Chat;
using Qmmands;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class MainCommandModule : ModuleBase<CommandContext>
    {
        public CommandService Service { get; set; }

        [Command("help", "commands")]
        [Description("Lists available commands.")]
        public async Task HelpAsync()
        {
            foreach (var cmd in Service.GetAllCommands())
            {
                await Context.Client.SendChatAsync($"{ChatColor.DarkGreen}{cmd.Name}{ChatColor.Reset}: {cmd.Description}");
            }

        }

        [Command("plugins")]
        [Description("Lists plugins.")]
        public async Task PluginsAsync()
        {
            var pls = string.Join(", ", Context.Server.PluginManager.Plugins.Select(x
                => $"{ChatColor.DarkGreen}{x.Info.Name}{ChatColor.Reset}"));
            await Context.Client.SendChatAsync($"{ChatColor.Gold}List of plugins: {pls}");
        }

        [Command("echo")]
        [Description("Echoes given text.")]
        public Task EchoAsync([Remainder] string text)
            => Context.Server.SendChatAsync(text, Context.Client, system: true);

        [Command("announce")]
        [Description("makes an announcement")]
        public Task AnnounceAsync([Remainder] string text)
            => Context.Server.SendChatAsync(text, Context.Client, 2, true);

        [Command("leave", "kickme")]
        [Description("kicks you")]
        public Task LeaveAsync()
            => Context.Client.DisconnectAsync(ChatMessage.Simple("Is this what you wanted?"));

        [Command("uptime", "up")]
        [Description("Gets current uptime")]
        public Task UptimeAsync()
            => Context.Client.SendChatAsync($"Uptime: {DateTimeOffset.Now.Subtract(Context.Server.StartTime).ToString()}");

        [Command("declarecmds", "declarecommands")]
        [Description("Debug command for testing the Declare Commands packet")]
        public Task DeclareCommandsTestAsync() => Context.Client.SendDeclareCommandsAsync();

        [Command("bossbar")]
        [Description("Debug command")]
        public Task TestBossBarAsync() => Context.Client.SendBossBarAsync(Guid.NewGuid(), new BossBarAddAction()
        {
            Color = BossBarColor.Blue,
            Division = BossBarDivisionType.None,
            Flags = BossBarFlags.DarkenSky,
            Title = ChatMessage.Simple("SUCC"),
            Health = 0.5f
        });
    }
}
