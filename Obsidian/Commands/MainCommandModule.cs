using Obsidian.Chat;
using Qmmands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Obsidian.Net.Packets.Play;
using Obsidian.Util.DataTypes;

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
                        ClickEvent = new TextComponent { Action = ETextAction.open_url, Value = pls.Info.ProjectUrl }
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

        [Command("spawnmob")]
        public async Task SpawnMob()
        {
            await Context.Client.SendSpawnMobAsync(3, Guid.NewGuid(), 1, new Transform
            {
                X = 0,

                Y = 105,

                Z = 0,

                Pitch = 0,

                Yaw = 0
            }, 0, new Velocity(0, 0, 0), Context.Client.Player);

            await Context.Player.SendMessageAsync("Spawning mob?");
        }

        [Command("forcechunkreload")]
        public async Task ForceChunkReloadAsync()
        {
            var c = Context.Client;
            var world = Context.Server.world;

            int dist = c.ClientSettings?.ViewDistance ?? 1;

            int oldchunkx = world.TransformToChunk(c.Player.PreviousTransform?.X ?? int.MaxValue);
            int chunkx = world.TransformToChunk(c.Player.Transform?.X ?? 0);

            int oldchunkz = world.TransformToChunk(c.Player.PreviousTransform?.Z ?? int.MaxValue);
            int chunkz = world.TransformToChunk(c.Player.Transform?.Z ?? 0);

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
        public Task LeaveAsync()
            => Context.Client.DisconnectAsync(ChatMessage.Simple("Is this what you wanted?"));

        [Command("uptime", "up")]
        [Description("Gets current uptime")]
        public Task UptimeAsync()
            => Context.Player.SendMessageAsync($"Uptime: {DateTimeOffset.Now.Subtract(Context.Server.StartTime).ToString()}");

        [Command("declarecmds", "declarecommands")]
        [Description("Debug command for testing the Declare Commands packet")]
        public Task DeclareCommandsTestAsync() => Context.Client.SendDeclareCommandsAsync();

        [Command("tp")]
        [Description("teleports you to a location")]
        public async Task TeleportAsync(Position location)
        {
            await Context.Player.SendMessageAsync("ight homie tryna tp you (and sip dicks)");
            await Context.Client.SendPlayerLookPositionAsync(new Transform(location.X, location.Y, location.Z), PositionFlags.NONE);
        }

        [Command("op")]
        [RequireOperator]
        public async Task GiveOpAsync(string username)
        {
            var player = Context.Server.OnlinePlayers.Values.FirstOrDefault(c => c.Username == username);
            if (player != null)
            {
                Context.Server.Operators.AddOperator(player);
            }
            else
            {
                Context.Server.Operators.AddOperator(username);
            }

            await Context.Player.SendMessageAsync($"Made {username} a server operator");
        }

        [Command("deop")]
        [RequireOperator]
        public async Task UnclaimOpAsync(string username)
        {
            var player = Context.Server.OnlinePlayers.Values.FirstOrDefault(c => c.Username == username);
            if (player != null)
            {
                Context.Server.Operators.AddOperator(player);
            }
            else
            {
                Context.Server.Operators.AddOperator(username);
            }

            await Context.Player.SendMessageAsync($"Made {username} no longer a server operator");
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