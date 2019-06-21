using Obsidian.Boss;
using Obsidian.Chat;
using Qmmands;
using System;
using System.Diagnostics;
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

        [Command("tp")]
        [Description("teleports you to a location")]
        public async Task TeleportAsync(double x, double y, double z)
        {
            await Context.Client.SendChatAsync("ight homie tryna tp you (and sip dicks)");
            await Context.Client.SendPlayerLookPositionAsync(new Util.Transform(x, y, z), Net.Packets.PositionFlags.NONE);
        }

        #if DEBUG
        [Command("claimop")]
        public async Task ClaimOpAsync()
        {
            Context.Server.Operators.AddOperator(Context.Player);
            await Context.Client.SendChatAsync("You are now OP!");
        }

        [Command("unclaimop")]
        public async Task UnclaimOpAsync()
        {
            Context.Server.Operators.RemoveOperator(Context.Player);
            await Context.Client.SendChatAsync("You are no longer OP!");
        }

        [Command("breakpoint")]
        public async Task BreakpointAsync()
        {
            await Context.Server.SendChatAsync("You might get kicked due to timeout, a breakpoint will hit in 3 seconds!", null, 0, true);
            await Task.Delay(3000);
            Debugger.Break();
        }
        #endif
    }
}
