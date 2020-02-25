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
                messages.Add(new ChatMessage
                {
                    Text = ChatColor.DarkGreen + pls.Info.Name + $"{ChatColor.Reset}, ",
                    ClickEvent = new TextComponent { Action = ETextAction.open_url, Value = pls.Info.ProjectUrl }
                });

            if (messages.Count > 0)
                message.AddExtra(messages);

            Console.WriteLine(message.ToString());

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

            int oldchunkx = world.transformToChunk(c.Player.PreviousTransform?.X ?? int.MaxValue);
            int chunkx = world.transformToChunk(c.Player.Transform?.X ?? 0);

            int oldchunkz = world.transformToChunk(c.Player.PreviousTransform?.Z ?? int.MaxValue);
            int chunkz = world.transformToChunk(c.Player.Transform?.Z ?? 0);

            await world.ResendBaseChunksAsync(dist, oldchunkx, oldchunkz, chunkx, chunkz, c);
        }

        [Command("echo")]
        [Description("Echoes given text.")]
        public Task EchoAsync([Remainder] string text)
        {
            Context.Server.Broadcast(text);

            return Task.CompletedTask;
        }

        [Command("announce")]
        [Description("makes an announcement")]
        public Task AnnounceAsync([Remainder] string text)
        {
            Context.Server.Broadcast(text, 2);

            return Task.CompletedTask;
        }

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
        public async Task TeleportAsync(double x, double y, double z)
        {
            await Context.Player.SendMessageAsync("ight homie tryna tp you (and sip dicks)");
            await Context.Client.SendPlayerLookPositionAsync(new Transform(x, y, z), PositionFlags.NONE);
        }

        [Command("op")]
        [RequireOperator]
        public async Task GiveOpAsync(string username)
        {
            var client = Context.Server.Clients.FirstOrDefault(c => c.Player != null && c.Player.Username == username);
            if (client != null)
            {
                Context.Server.Operators.AddOperator(client.Player);
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
            var client = Context.Server.Clients.FirstOrDefault(c => c.Player != null && c.Player.Username == username);
            if (client != null)
            {
                Context.Server.Operators.AddOperator(client.Player);
            }
            else
            {
                Context.Server.Operators.AddOperator(username);
            }

            await Context.Player.SendMessageAsync($"Made {username} no longer a server operator");
        }

        [Command("oprequest", "opreq")]
        public async Task RequestOpAsync()
        {
            if (!Context.Server.Config.AllowOperatorRequests)
            {
                await Context.Player.SendMessageAsync("§cOperator requests are disabled on this server.");
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

        [Command("oprequest", "opreq")]
        public async Task RequestOpAsync(string code)
        {
            if (!Context.Server.Config.AllowOperatorRequests)
            {
                await Context.Player.SendMessageAsync("§cOperator requests are disabled on this server.");
                return;
            }

            if (Context.Server.Operators.ProcessRequest(Context.Player, code))
            {
                await Context.Player.SendMessageAsync("Your request has been accepted");
            }
            else
            {
                await Context.Player.SendMessageAsync("§cInvalid request");
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
            Context.Server.Broadcast("You might get kicked due to timeout, a breakpoint will hit in 3 seconds!");
            await Task.Delay(3000);
            Debugger.Break();
        }

#endif
    }
}