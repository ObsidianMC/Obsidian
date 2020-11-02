using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.Chat;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using Obsidian.Entities;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class MainCommandModule : BaseCommandClass
    {
        #region help
        [Command("help", "commands")]
        [CommandInfo("Lists available commands.", "/help [<page>]")]
        public async Task HelpAsync(ObsidianContext Context)
        {
            var player = (Player)Context.Player;
            var commands = ChatMessage.Simple("");
            var header = new ChatMessage()
            {
                Underline = true,
                Text = $"List of available commands:"
            };
            commands.AddExtra(header);
            foreach (var cmd in Context.Commands.GetAllCommands().Where(x => x.Parent == null))
            {
                // only list commands the user may execute.
                var success = true;
                foreach (var check in cmd.ExecutionChecks)
                {
                    if (!await check.RunChecksAsync(Context))
                    {
                        // at least one check failed
                        success = false;
                    }
                }

                if (success)
                {
                    var commandName = new ChatMessage
                    {
                        Text = $"\n{ChatColor.Gold}{(cmd.Usage == "" ? cmd.Name: cmd.Usage)}",
                        ClickEvent = new TextComponent
                        {
                            Action = ETextAction.SuggestCommand,
                            Value = $"{(cmd.Usage == "" ? $"{cmd.Name} " : cmd.Usage.Contains(" ") ? $"{cmd.Usage.Split(" ")[0]} ": cmd.Usage)}"
                        },
                        HoverEvent = new TextComponent
                        {
                            Action = ETextAction.ShowText,
                            Value = $"Click to suggest the command"
                        }
                    };
                    commands.AddExtra(commandName);

                    var commandInfo = new ChatMessage
                    {
                        Text = $"{ChatColor.Gray}:{ChatColor.Reset} {cmd.Description}"
                    };
                    commands.AddExtra(commandInfo);
                }
            }

            await player.SendMessageAsync(commands);
        }

        [CommandOverload]
        public async Task HelpAsync(ObsidianContext Context, [Remaining] string args_)
        {
            var args = args_.Contains(" ") ? args_.Split(" ").ToList() : new List<string> { args_ };
            // TODO subcommand help
            await Context.Player.SendMessageAsync($"Help command arguments: {string.Join(" ", args)}");
        }
        #endregion

        #region tps
        [Command("tps")]
        [CommandInfo("Gets server TPS", "/tps")]
        public async Task TPSAsync(ObsidianContext ctx)
        {
            ChatColor color;

            if (ctx.Server.TPS > 15) color = ChatColor.BrightGreen;
            else if (ctx.Server.TPS > 10) color = ChatColor.Yellow;
            else color = ChatColor.Red;

            var message = new ChatMessage
            {
                Text = $"{ChatColor.Gold}Current server TPS: {color}{ctx.Server.TPS}",
            };
            await ctx.Player.SendMessageAsync(message.ToString());

        }
        #endregion

        #region group
        [CommandGroup("group")]
        [CommandInfo("Test group command", "/group [...]")]
        public class Group
        {
            [GroupCommand]
            public async Task ExecuteAsync(ObsidianContext ctx)
            {
                await ctx.Player.SendMessageAsync("group command", 1);
            }

            [GroupCommand]
            public async Task ExecuteAsync(ObsidianContext ctx, int test)
            {
                await ctx.Player.SendMessageAsync($"group command overload {test}", 1);
            }

            [Command("sub")]
            public async Task SubCommandAsync(ObsidianContext ctx)
            {
                await ctx.Player.SendMessageAsync("group subcommand", 1);
            }

            [CommandOverload]
            public async Task SubCommandAsync(ObsidianContext ctx, int test)
            {
                await ctx.Player.SendMessageAsync($"group subcommand overload {test}", 1);
            }
        }
        #endregion

        #region test
        [Command("test")]
        [CommandInfo("Test message", "/test <test1> <test2> <test3>")]
        public async Task TestAsync(ObsidianContext Context, string test1, string test2, string test3)
        {
            await Context.Player.SendMessageAsync($"{test1} + {test2} + {test3}");
        }
        #endregion

        #region plugins
        [Command("plugins", "pl")]
        [CommandInfo("Gets all plugins", "/plugins")]
        public async Task PluginsAsync(ObsidianContext Context)
        {
            var srv = (Server)Context.Server;
            var player = (Player)Context.Player;
            var pluginCount = srv.PluginManager.Plugins.Count;
            var message = new ChatMessage
            {
                Text = $"{ChatColor.Reset}List of plugins ({ChatColor.Gold}{pluginCount}{ChatColor.Reset}): ",
            };

            var messages = new List<ChatMessage>();

            for (int i = 0; i < pluginCount; i++)
            {
                var pluginContainer = srv.PluginManager.Plugins[i];
                var info = pluginContainer.Info;

                var plugin = new ChatMessage();
                var colorByState = pluginContainer.Loaded || pluginContainer.IsReady ? ChatColor.DarkGreen : ChatColor.DarkRed;
                plugin.Text = colorByState + pluginContainer.Info.Name;

                plugin.HoverEvent = new TextComponent { Action = ETextAction.ShowText, Value = $"{colorByState}{info.Name}{ChatColor.Reset}\nVersion: {colorByState}{info.Version}{ChatColor.Reset}\nAuthor(s): {colorByState}{info.Authors}{ChatColor.Reset}" };
                if (pluginContainer.Info.ProjectUrl != null)
                    plugin.ClickEvent = new TextComponent { Action = ETextAction.OpenUrl, Value = pluginContainer.Info.ProjectUrl.AbsoluteUri };

                messages.Add(plugin);

                messages.Add(new ChatMessage
                {
                    Text = $"{ChatColor.Reset}{(i + 1 < srv.PluginManager.Plugins.Count ? ", " : "")}"
                });
            }
            if (messages.Count > 0)
                message.AddExtra(messages);

            await player.SendMessageAsync(message);
        }
        #endregion

        #region forcechunkreload
        [Command("forcechunkreload")]
        [CommandInfo("Force chunk reload", "/forcechunkreload")]
        public async Task ForceChunkReloadAsync(ObsidianContext Context)
        {
            var player = (Player)Context.Player;
            var server = (Server)Context.Server;
            var c = player.client;
            var world = server.World;

            int dist = c.ClientSettings?.ViewDistance ?? 1;
            (int oldChunkX, int oldChunkZ) = c.Player.LastLocation.ToChunkCoord();
            (int chunkX, int chunkZ) = c.Player.Location.ToChunkCoord();

            await world.ResendBaseChunksAsync(dist, oldChunkX, oldChunkZ, chunkX, chunkZ, c);
        }
        #endregion

        #region echo
        [Command("echo")]
        [CommandInfo("Echoes given text.", "/echo <message>")]
        public Task EchoAsync(ObsidianContext Context, [Remaining] string text) => Context.Server.BroadcastAsync(text);
        #endregion

        #region announce
        [Command("announce")]
        [CommandInfo("Makes an announcement", "/announce <message>")]
        public Task AnnounceAsync(ObsidianContext Context, [Remaining] string text) => Context.Server.BroadcastAsync(text, 2);
        #endregion

        #region leave
        [Command("leave", "kickme")]
        [CommandInfo("kicks you", "/leave")]
        public Task LeaveAsync(ObsidianContext Context) => Context.Player.KickAsync("Is this what you wanted?");
        #endregion

        #region uptime
        [Command("uptime", "up")]
        [CommandInfo("Gets current uptime", "/uptime")]
        public Task UptimeAsync(ObsidianContext Context)
            => Context.Player.SendMessageAsync($"Uptime: {DateTimeOffset.Now.Subtract(Context.Server.StartTime)}");
        #endregion

        #region declarecmds
        [Command("declarecmds", "declarecommands")]
        [CommandInfo("Debug command for testing the Declare Commands packet", "/declarecmds")]
        public Task DeclareCommandsTestAsync(ObsidianContext Context) => ((Player)Context.Player).client.SendDeclareCommandsAsync();
        #endregion

        #region gamemode
        [Command("gamemode")]
        [CommandInfo("Change your gamemode.", "/gamemode <survival/creative/adventure/spectator>")]
        public async Task GamemodeAsync(ObsidianContext Context)
        {
            var chatMessage = SendCommandUsage("/gamemode <survival/creative/adventure/spectator>");
            await Context.Player.SendMessageAsync(chatMessage);
        }

        [CommandOverload]
        public async Task GamemodeAsync(ObsidianContext Context, [Remaining] string args_)
        {
            var chatMessage = ChatMessage.Simple("");
            var args = args_.Contains(" ") ? args_.Split(" ").ToList() : new List<string> { args_ };
            if (args.Count == 1)
            {
                if (args[0].ToLower() == "creative" || args[0].ToLower() == "survival" || args[0].ToLower() == "spectator" || args[0].ToLower() == "adventure")
                {
                    try
                    {
                        var gamemode = (Gamemode)Enum.Parse(typeof(Gamemode), args[0], true);
                        if (Context.Player.Gamemode != gamemode)
                        {
                            Context.Player.Gamemode = gamemode;
                            chatMessage = ChatMessage.Simple($"{ChatColor.Reset}Your game mode set to {ChatColor.Red}{gamemode}{ChatColor.Reset}.");
                        }
                        else
                        {
                            Context.Player.Gamemode = gamemode;
                            chatMessage = ChatMessage.Simple($"{ChatColor.Reset}Your current game mode is {ChatColor.Red}{gamemode}{ChatColor.Reset}.");
                        }
                    }
                    catch (Exception)
                    {
                        chatMessage = SendCommandUsage("/gamemode <survival/creative/adventure/spectator>");
                    }
                }
            }
            else
            {
                chatMessage = SendCommandUsage("/gamemode <survival/creative/adventure/spectator>");
            }
            await Context.Player.SendMessageAsync(chatMessage);
        }
        #endregion

        #region tp
        [Command("tp")]
        [CommandInfo("teleports you to a location", "/tp <x> <y> <z>")]
        public async Task TeleportAsync(ObsidianContext Context, [Remaining] Position location)
        {
            var player = (Player)Context.Player;
            await player.SendMessageAsync($"ight homie tryna tp you (and sip dicks) {location.X} {location.Y} {location.Z}");
            await player.TeleportAsync(location);
        }
        #endregion

        #region op
        [Command("op")]
        [CommandInfo("Give operator rights to a specific player.", "/op <player>")]
        [RequireOperator]
        public async Task GiveOpAsync(ObsidianContext Context, IPlayer player)
        {
            var server = (Server)Context.Server;
            var onlinePlayers = server.OnlinePlayers;
            if (!onlinePlayers.ContainsKey(player.Uuid) || !onlinePlayers.Any(x => x.Value.Username == player.Username))
                return;

            server.Operators.AddOperator((Player)player);

            await Context.Player.SendMessageAsync($"Made {player} a server operator");
        }
        #endregion

        #region deop
        [Command("deop")]
        [CommandInfo("Remove specific player's operator rights.", "/deop <player>")]
        [RequireOperator]
        public async Task UnclaimOpAsync(ObsidianContext Context, IPlayer player)
        {
            var server = (Server)Context.Server;
            var onlinePlayers = server.OnlinePlayers;
            if (!onlinePlayers.ContainsKey(player.Uuid) || !onlinePlayers.Any(x => x.Value.Username == player.Username))
                return;

            server.Operators.RemoveOperator((Player)player);

            await Context.Player.SendMessageAsync($"Made {player} no longer a server operator");
        }
        #endregion

        #region oprequest
        [Command("oprequest", "opreq")]
        [CommandInfo("Request operator rights.", "/oprequest [<code>]")]
        public async Task RequestOpAsync(ObsidianContext Context, string code = null)
        {
            var server = (Server)Context.Server;
            var player = (Player)Context.Player;

            if (!server.Config.AllowOperatorRequests)
            {
                await player.SendMessageAsync("§cOperator requests are disabled on this server.");
                return;
            }

            if (server.Operators.ProcessRequest(player, code))
            {
                await player.SendMessageAsync("Your request has been accepted");

                return;
            }

            if (server.Operators.CreateRequest(player))
            {
                await Context.Player.SendMessageAsync("A request has been to the server console");
            }
            else
            {
                await Context.Player.SendMessageAsync("§cYou have already sent a request");
            }
        }
        #endregion

        #region obsidian
        [Command("obsidian")]
        [CommandInfo("Shows obsidian popup", "/obsidian")]
        public async Task ObsidianAsync(ObsidianContext Context)
        {
            await Context.Player.SendMessageAsync("§dWelcome to Obsidian Test Build. §l§4<3", 2);
        }
        #endregion

        #region stop
        [Command("stop")]
        [CommandInfo("Stops the server.", "/stop")]
        [RequireOperator]
        public async Task StopAsync(ObsidianContext Context)
        {
            await Context.Server.BroadcastAsync($"Server stopped by {ChatColor.Red}{Context.Player.Username}{ChatColor.Reset}.");
            await Task.Run(() =>
            {
                Context.Server.StopServer();
            });
        }
        #endregion

#if DEBUG
        #region breakpoint
        [Command("breakpoint")]
        [CommandInfo("Creats a breakpoint to help debug", "/breakpoint")]
        [RequireOperator]
        public async Task BreakpointAsync(ObsidianContext Context)
        {
            await Context.Server.BroadcastAsync("You might get kicked due to timeout, a breakpoint will hit in 3 seconds!");
            await Task.Delay(3000);
            Debugger.Break();
        }
        #endregion
#endif

        #region Render command usage
        private ChatMessage SendCommandUsage(string commandUsage)
        {
            var commands = ChatMessage.Simple("");
            var commandSuggest = commandUsage.Contains(" ") ? $"{commandUsage.Split(" ").FirstOrDefault()} " : commandUsage;
            var usage = new ChatMessage
            {
                Text = $"{ChatColor.Red}{commandUsage}",
                ClickEvent = new TextComponent
                {
                    Action = ETextAction.SuggestCommand,
                    Value = $"{commandSuggest}"
                },
                HoverEvent = new TextComponent
                {
                    Action = ETextAction.ShowText,
                    Value = $"Click to suggest the command"
                }
            };

            var prefix = new ChatMessage
            {
                Text = $"{ChatColor.Red}Usage: "
            };

            commands.AddExtra(prefix);
            commands.AddExtra(usage);
            return commands;
        }
        #endregion
    }
}