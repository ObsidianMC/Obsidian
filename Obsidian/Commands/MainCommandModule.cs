using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Util.DataTypes;
using Qmmands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class MainCommandModule : ModuleBase<ObsidianContext>
    {
        public CommandService Service { get; set; }

        [Command("help", "commands")]
        [Description("Lists available commands.")]
        public async Task HelpAsync()
        {
            foreach (var cmd in Service.GetAllCommands())
            {
                await Context.Player.SendMessageAsync($"{ChatColor.DarkGreen}{cmd.Name}{ChatColor.Reset}: {cmd.Description}");
            }
        }

        [Command("forceskins")]
        [Description("forces skin reload")]
        public async Task ForceSkinAsync()
        {
            await Context.Client.SendPlayerInfoAsync();
            await Context.Player.SendMessageAsync(ChatMessage.Simple("done"));
        }

        [Command("test")]
        public async Task TestAsync(string test1, string test2, string test3)
        {
            await this.Context.Player.SendMessageAsync($"{test1} + {test2} + {test3}");
        }

        [Command("plugins")]
        [Description("Lists plugins.")]
        public async Task PluginsAsync()
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
        public async Task ForceChunkReloadAsync()
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
        [Description("Echoes given text.")]
        public Task EchoAsync([Remainder] string text) => Context.Server.BroadcastAsync(text);

        [Command("announce")]
        [Description("makes an announcement")]
        public Task AnnounceAsync([Remainder] string text) => Context.Server.BroadcastAsync(text, 2);

        [Command("leave", "kickme")]
        [Description("kicks you")]
        public Task LeaveAsync() => this.Context.Player.KickAsync("Is this what you wanted?");

        [Command("uptime", "up")]
        [Description("Gets current uptime")]
        public Task UptimeAsync()
            => Context.Player.SendMessageAsync($"Uptime: {DateTimeOffset.Now.Subtract(Context.Server.StartTime)}");

        [Command("declarecmds", "declarecommands")]
        [Description("Debug command for testing the Declare Commands packet")]
        public Task DeclareCommandsTestAsync() => Context.Client.SendDeclareCommandsAsync();

        [Command("tp")]
        [Description("teleports you to a location")]
        public async Task TeleportAsync(Position location)
        {
            await Context.Player.SendMessageAsync("ight homie tryna tp you (and sip dicks)");
            await this.Context.Player.TeleportAsync(location);
        }

        [Command("op")]
        [RequireOperator]
        public async Task GiveOpAsync(Player player)
        {
            var onlinePlayers = this.Context.Server.OnlinePlayers;
            if (!onlinePlayers.ContainsKey(player.Uuid) || !onlinePlayers.Any(x => x.Value.Username == player.Username))
                return;

            Context.Server.Operators.AddOperator(player);

            await Context.Player.SendMessageAsync($"Made {player} a server operator");
        }

        [Command("deop")]
        [RequireOperator]
        public async Task UnclaimOpAsync(Player player)
        {
            var onlinePlayers = this.Context.Server.OnlinePlayers;
            if (!onlinePlayers.ContainsKey(player.Uuid) || !onlinePlayers.Any(x => x.Value.Username == player.Username))
                return;

            Context.Server.Operators.RemoveOperator(player);

            await Context.Player.SendMessageAsync($"Made {player} no longer a server operator");
        }

        [Command("oprequest", "opreq")]
        public async Task RequestOpAsync(string code = null)
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
        [Description("Shows obsidian popup")]
        public async Task ObsidianAsync()
        {
            await Context.Player.SendMessageAsync("§dWelcome to Obsidian Test Build. §l§4<3", 2);
        }

#if DEBUG

        [Command("breakpoint")]
        public async Task BreakpointAsync()
        {
            await Context.Server.BroadcastAsync("You might get kicked due to timeout, a breakpoint will hit in 3 seconds!");
            await Task.Delay(3000);
            Debugger.Break();
        }

#endif
    }
}