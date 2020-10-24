using Obsidian.Chat;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Util.DataTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class MainCommandModule : BaseCommandClass
    {
        [Command("help", "commands")]
        [CommandInfo("Lists available commands.")]
        public async Task HelpAsync(ObsidianContext Context)
        {
            await Context.Player.SendMessageAsync(new ChatMessage() { Bold = true, Underline = true, Text = $"***Command Listing***" });
            foreach (var cmd in Context.Commands.GetAllCommands())
            {
                await Context.Player.SendMessageAsync($"{ChatColor.DarkGreen}{cmd.CommandName}{ChatColor.Reset}: {cmd.Description}");
            }
        }

        [Command("forceskins")]
        [CommandInfo("forces skin reload")]
        public async Task ForceSkinAsync(ObsidianContext Context)
        {
            await Context.Client.SendPlayerInfoAsync();
            await Context.Player.SendMessageAsync(ChatMessage.Simple("done"));
        }

        [Command("test")]
        public async Task TestAsync(ObsidianContext Context, string test1, string test2, string test3)
        {
            await Context.Player.SendMessageAsync($"{test1} + {test2} + {test3}");
        }

        [Command("plugins")]
        [CommandInfo("Lists plugins.")]
        public async Task PluginsAsync(ObsidianContext Context)
        {
            var message = new ChatMessage
            {
                Text = $"{ChatColor.Gold}List of plugins: ",
            };

            var messages = new List<ChatMessage>();

            foreach (var pls in Context.Server.PluginManager.Plugins)
                if (pls.Info.ProjectUrl != null)
                {
                    messages.Add(new ChatMessage
                    {
                        Text = ChatColor.DarkGreen + pls.Info.Name + $"{ChatColor.Reset}, ",
                        ClickEvent = new TextComponent { Action = ETextAction.OpenUrl, Value = pls.Info.ProjectUrl }
                    });
                }
                else
                {
                    messages.Add(new ChatMessage
                    {
                        Text = ChatColor.DarkGreen + pls.Info.Name + $"{ChatColor.Reset}, "
                    });
                }

            if (messages.Count > 0)
                message.AddExtra(messages);

            await Context.Player.SendMessageAsync(message);
            //await Context.Player.SendMessageAsync(pls);
        }

        [Command("forcechunkreload")]
        public async Task ForceChunkReloadAsync(ObsidianContext Context)
        {
            var c = Context.Client;
            var world = Context.Server.World;

            int dist = c.ClientSettings?.ViewDistance ?? 1;

            int oldchunkx = world.TransformToChunk(c.Player.LastLocation?.X ?? int.MaxValue);
            int chunkx = world.TransformToChunk(c.Player.Location?.X ?? 0);

            int oldchunkz = world.TransformToChunk(c.Player.LastLocation?.Z ?? int.MaxValue);
            int chunkz = world.TransformToChunk(c.Player.Location?.Z ?? 0);

            await world.ResendBaseChunksAsync(dist, oldchunkx, oldchunkz, chunkx, chunkz, c);
        }

        [Command("echo")]
        [CommandInfo("Echoes given text.")]
        public Task EchoAsync(ObsidianContext Context, [Remaining] string text) => Context.Server.BroadcastAsync(text);

        [Command("announce")]
        [CommandInfo("makes an announcement")]
        public Task AnnounceAsync(ObsidianContext Context, [Remaining] string text) => Context.Server.BroadcastAsync(text, 2);

        [Command("leave", "kickme")]
        [CommandInfo("kicks you")]
        public Task LeaveAsync(ObsidianContext Context) => Context.Player.KickAsync("Is this what you wanted?");

        [Command("uptime", "up")]
        [CommandInfo("Gets current uptime")]
        public Task UptimeAsync(ObsidianContext Context)
            => Context.Player.SendMessageAsync($"Uptime: {DateTimeOffset.Now.Subtract(Context.Server.StartTime)}");

        [Command("declarecmds", "declarecommands")]
        [CommandInfo("Debug command for testing the Declare Commands packet")]
        public Task DeclareCommandsTestAsync(ObsidianContext Context) => Context.Client.SendDeclareCommandsAsync();

        [Command("tp")]
        [CommandInfo("teleports you to a location")]
        public async Task TeleportAsync(ObsidianContext Context, [Remaining]Position location)
        {
            await Context.Player.SendMessageAsync($"ight homie tryna tp you (and sip dicks) {location.X} {location.Y} {location.Z}");
            await Context.Player.TeleportAsync(location);
        }

        [Command("op")]
        [RequireOperator]
        public async Task GiveOpAsync(ObsidianContext Context, Player player)
        {
            var onlinePlayers = Context.Server.OnlinePlayers;
            if (!onlinePlayers.ContainsKey(player.Uuid) || !onlinePlayers.Any(x => x.Value.Username == player.Username))
                return;

            Context.Server.Operators.AddOperator(player);

            await Context.Player.SendMessageAsync($"Made {player} a server operator");
        }

        [Command("deop")]
        [RequireOperator]
        public async Task UnclaimOpAsync(ObsidianContext Context, Player player)
        {
            var onlinePlayers = Context.Server.OnlinePlayers;
            if (!onlinePlayers.ContainsKey(player.Uuid) || !onlinePlayers.Any(x => x.Value.Username == player.Username))
                return;

            Context.Server.Operators.RemoveOperator(player);

            await Context.Player.SendMessageAsync($"Made {player} no longer a server operator");
        }

        [Command("oprequest", "opreq")]
        public async Task RequestOpAsync(ObsidianContext Context, string code = null)
        {
            if (!Context.Server.Config.AllowOperatorRequests)
            {
                await Context.Player.SendMessageAsync("§cOperator requests are disabled on this server.");
                return;
            }

            if (Context.Server.Operators.ProcessRequest(Context.Player, code))
            {
                await Context.Player.SendMessageAsync("Your request has been accepted");

                return;
            }

            if (Context.Server.Operators.CreateRequest(Context.Player))
            {
                await Context.Player.SendMessageAsync("A request has been to the server console");
            }
            else
            {
                await Context.Player.SendMessageAsync("§cYou have already sent a request");
            }
        }

        [Command("obsidian")]
        [CommandInfo("Shows obsidian popup")]
        public async Task ObsidianAsync(ObsidianContext Context)
        {
            await Context.Player.SendMessageAsync("§dWelcome to Obsidian Test Build. §l§4<3", 2);
        }

#if DEBUG

        [Command("breakpoint")]
        [RequireOperator]
        public async Task BreakpointAsync(ObsidianContext Context)
        {
            await Context.Server.BroadcastAsync("You might get kicked due to timeout, a breakpoint will hit in 3 seconds!");
            await Task.Delay(3000);
            Debugger.Break();
        }

#endif
    }
}