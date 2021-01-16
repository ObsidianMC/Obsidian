using Obsidian.API;
using Obsidian.Chat;
using Obsidian.CommandFramework;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using Obsidian.Entities;
using Obsidian.Util.Registry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class MainCommandModule : BaseCommandClass
    {
        #region help
        private const int CommandsPerPage = 15;
        [Command("help", "commands")]
        [CommandInfo("Lists available commands.", "/help [<page>]")]
        public async Task HelpAsync(ObsidianContext Context) => await HelpAsync(Context, 1);
        [CommandOverload]
        public async Task HelpAsync(ObsidianContext Context, int page)
        {
            var player = (Player)Context.Player;
            var allcommands = Context.Commands.GetAllCommands();
            var availablecommands = new List<Command>();

            // filter available commands
            foreach (var cmd in allcommands)
            {
                var success = true;
                // check commands
                // only list commands the user may execute.
                foreach (var check in cmd.ExecutionChecks)
                {
                    if (!await check.RunChecksAsync(Context))
                    {
                        success = false;
                    }
                }
                if (success)
                    availablecommands.Add(cmd);
            }

            int commandcount = availablecommands.Count;

            var remainder = commandcount % CommandsPerPage;
            int pagecount = (commandcount - remainder) / CommandsPerPage; // all commands / page commands - remainder
            if (remainder > 0)
                pagecount++; // if remainder, extra page

            if (page < 1 || page > pagecount)
            {
                await player.SendMessageAsync(ChatMessage.Simple($"{ChatColor.Red}Invalid help page."));
                return;
            }

            var cmdsection = availablecommands.Skip((page - 1) * CommandsPerPage).Take(CommandsPerPage);

            var commands = ChatMessage.Simple("\n");
            var header = new ChatMessage()
            {
                Underline = true,
                Text = $"List of available commands ({page}/{pagecount}):"
            };
            commands.AddExtra(header);
            foreach (var cmd in cmdsection.Where(x => x.Parent == null))
            {

                var commandName = new ChatMessage
                {
                    Text = $"\n{ChatColor.Gold}{(cmd.Usage == "" ? $"/{cmd.Name}" : cmd.Usage)}",
                    ClickEvent = new TextComponent
                    {
                        Action = ETextAction.SuggestCommand,
                        Value = $"{(cmd.Usage == "" ? $"/{cmd.Name}" : cmd.Usage.Contains(" ") ? $"{cmd.Usage.Split(" ")[0]} " : cmd.Usage)}"
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

            await player.SendMessageAsync(commands);
        }
        #endregion

        #region tps
        [Command("tps")]
        [CommandInfo("Gets server TPS", "/tps")]
        public async Task TPSAsync(ObsidianContext ctx)
        {
            ChatColor color;
            var player = (Player)ctx.Player;

            if (ctx.Server.TPS > 15) color = ChatColor.BrightGreen;
            else if (ctx.Server.TPS > 10) color = ChatColor.Yellow;
            else color = ChatColor.Red;

            var message = new ChatMessage
            {
                Text = $"{ChatColor.Gold}Current server TPS: {color}{ctx.Server.TPS}",
            };
            await player.SendMessageAsync(message);

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

            await world.UpdateClientChunksAsync(c, true);
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
        [RequirePermission(op: true, permissions: "obsidian.announce")]
        public Task AnnounceAsync(ObsidianContext Context, [Remaining] string text) => Context.Server.BroadcastAsync(text, MessageType.ActionBar);
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
        public Task DeclareCommandsTestAsync(ObsidianContext Context) => ((Player)Context.Player).client.QueuePacketAsync(Registry.DeclareCommandsPacket);
        #endregion

        #region gamemode
        [Command("gamemode")]
        [CommandInfo("Change your gamemode.", "/gamemode <survival/creative/adventure/spectator>")]
        public async Task GamemodeAsync(ObsidianContext Context)
        {
            var chatMessage = SendCommandUsage("/gamemode <survival/creative/adventure/spectator>");
            var player = (Player)Context.Player;
            await player.SendMessageAsync(chatMessage);
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
                            await Context.Player.SetGamemodeAsync(gamemode);
                            chatMessage = ChatMessage.Simple($"{ChatColor.Reset}Your game mode set to {ChatColor.Red}{gamemode}{ChatColor.Reset}.");
                        }
                        else
                        {
                            chatMessage = ChatMessage.Simple($"{ChatColor.Reset}Your're already in {ChatColor.Red}{gamemode}{ChatColor.Reset} game mode.");
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
            var player = (Player)Context.Player;
            await player.SendMessageAsync(chatMessage);
        }
        #endregion

        #region tp
        [Command("tp")]
        [CommandInfo("teleports you to a location", "/tp <x> <y> <z>")]
        public async Task TeleportAsync(ObsidianContext Context, [Remaining] PositionF location)
        {
            var player = (Player)Context.Player;
            await player.SendMessageAsync($"ight homie tryna tp you (and sip dicks) {location.X} {location.Y} {location.Z}");
            await player.TeleportAsync(location);
        }
        #endregion

        #region op
        [Command("op")]
        [CommandInfo("Give operator rights to a specific player.", "/op <player>")]
        [RequirePermission]
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
        [RequirePermission]
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
            await Context.Player.SendMessageAsync("§dWelcome to Obsidian Test Build. §l§4<3", MessageType.ActionBar);
        }
        #endregion

        #region stop
        [Command("stop")]
        [CommandInfo("Stops the server.", "/stop")]
        [RequirePermission(permissions: "obsidian.stop")]
        public async Task StopAsync(ObsidianContext Context)
        {
            var server = (Server)Context.Server;
            await server.BroadcastAsync($"Server stopped by {ChatColor.Red}{Context.Player.Username}{ChatColor.Reset}.");
            await Task.Run(() =>
            {
                server.StopServer();
            });
        }
        #endregion

        #region permissions
        [CommandGroup("permission")]
        [RequirePermission(permissions: "obsidian.permissions")]
        public class Permission
        {
            [GroupCommand]
            public async Task CheckPermission(ObsidianContext ctx, string permission)
            {
                if (await ctx.Player.HasPermission(permission))
                {
                    await ctx.Player.SendMessageAsync($"You have {ChatColor.BrightGreen}{permission}{ChatColor.Reset}.");
                }
                else
                {
                    await ctx.Player.SendMessageAsync($"You don't have {ChatColor.Red}{permission}{ChatColor.Reset}.");
                }
            }

            [Command("grant")]
            public async Task GrantPermission(ObsidianContext ctx, string permission)
            {
                if (await ctx.Player.GrantPermission(permission))
                {
                    await ctx.Player.SendMessageAsync($"Sucessfully granted {ChatColor.BrightGreen}{permission}{ChatColor.Reset}.");
                }
                else
                {
                    await ctx.Player.SendMessageAsync($"Failed granting {ChatColor.Red}{permission}{ChatColor.Reset}.");
                }
            }
            [Command("revoke")]
            public async Task RevokePermission(ObsidianContext ctx, string permission)
            {
                if (await ctx.Player.RevokePermission(permission))
                {
                    await ctx.Player.SendMessageAsync($"Sucessfully revoked {ChatColor.BrightGreen}{permission}{ChatColor.Reset}.");
                }
                else
                {
                    await ctx.Player.SendMessageAsync($"Failed revoking {ChatColor.Red}{permission}{ChatColor.Reset}.");
                }
            }
        }
        #endregion

#if DEBUG
        #region breakpoint
        [Command("breakpoint")]
        [CommandInfo("Creats a breakpoint to help debug", "/breakpoint")]
        [RequirePermission(op: true)]
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
