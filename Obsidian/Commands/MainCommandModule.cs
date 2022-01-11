using Obsidian.Commands.Framework.Entities;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData;
using System.Data;
using System.Diagnostics;

namespace Obsidian.Commands;

public class MainCommandModule
{
    private const int CommandsPerPage = 15;
    [Command("help", "commands")]
    [CommandInfo("Lists available commands.", "/help [<page>]")]
    public async Task HelpAsync(CommandContext Context) => await HelpAsync(Context, 1);

    [CommandOverload]
    public async Task HelpAsync(CommandContext Context, int page)
    {
        var sender = Context.Sender;
        var server = (Server)Context.Server;
        var commandHandler = server.CommandsHandler;
        var allCommands = commandHandler.GetAllCommands();
        var availableCommands = new List<Command>();

        // filter available commands
        foreach (var command in allCommands)
        {
            var success = true;
            // check commands
            // only list commands the user may execute.
            foreach (var check in command.ExecutionChecks)
            {
                if (!await check.RunChecksAsync(Context))
                {
                    success = false;
                }
            }
            if (success)
                availableCommands.Add(command);
        }

        int commandCount = availableCommands.Count;

        var remainder = commandCount % CommandsPerPage;
        int pagecount = (commandCount - remainder) / CommandsPerPage; // all commands / page commands - remainder
        if (remainder > 0)
            pagecount++; // if remainder, extra page

        if (page < 1 || page > pagecount)
        {
            await sender.SendMessageAsync(ChatMessage.Simple($"{ChatColor.Red}Invalid help page."));
            return;
        }

        var commandSection = availableCommands.Skip((page - 1) * CommandsPerPage).Take(CommandsPerPage);

        var commands = ChatMessage.Simple("\n");
        var header = new ChatMessage()
        {
            Underlined = true,
            Text = $"List of available commands ({page}/{pagecount}):"
        };
        commands.AddExtra(header);
        foreach (var cmd in commandSection.Where(x => x.Parent is null))
        {
            string usage = cmd.Usage.IsEmpty() ? $"/{cmd.Name}" : cmd.Usage;
            var commandName = new ChatMessage
            {
                Text = $"\n{ChatColor.Gold}{usage}",
                ClickEvent = new ClickComponent
                (
                    EClickAction.SuggestCommand,
                    usage.Contains(' ') ? $"{usage.Substring(0, usage.IndexOf(' '))} " : usage
                ),
                HoverEvent = new HoverComponent
                (
                    EHoverAction.ShowText,
                    $"Click to suggest the command"
                )
            };
            commands.AddExtra(commandName);

            if (!cmd.Description.IsNullOrEmpty())
            {
                commands.AddExtra(new ChatMessage
                {
                    Text = $"{ChatColor.Gray}:{ChatColor.Reset} {cmd.Description}"
                });
            }
        }
        await sender.SendMessageAsync(commands);
    }

    [Command("tps")]
    [CommandInfo("Gets server TPS", "/tps")]
    public async Task TPSAsync(CommandContext ctx)
    {
        ChatColor color;
        var sender = ctx.Sender;

        if (ctx.Server.Tps > 15) color = ChatColor.BrightGreen;
        else if (ctx.Server.Tps > 10) color = ChatColor.Yellow;
        else color = ChatColor.Red;

        var message = new ChatMessage
        {
            Text = $"{ChatColor.Gold}Current server TPS: {color}{ctx.Server.Tps}",
        };
        await sender.SendMessageAsync(message);

    }

    [Command("plugins", "pl")]
    [CommandInfo("Gets all plugins", "/plugins")]
    public async Task PluginsAsync(CommandContext Context)
    {
        var srv = (Server)Context.Server;
        var sender = Context.Sender;
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
            var colorByState = pluginContainer.Loaded || pluginContainer.IsReady ? HexColor.Green : HexColor.Red;

            plugin.Text = pluginContainer.Info.Name;
            plugin.Color = colorByState;

            plugin.HoverEvent = new HoverComponent
            (
                EHoverAction.ShowText,
                $"{colorByState}{info.Name}{ChatColor.Reset}\nVersion: {colorByState}{info.Version}{ChatColor.Reset}\nAuthor(s): {colorByState}{info.Authors}{ChatColor.Reset}"
            );

            if (pluginContainer.Info.ProjectUrl != null)
                plugin.ClickEvent = new ClickComponent(EClickAction.OpenUrl, pluginContainer.Info.ProjectUrl.AbsoluteUri);

            messages.Add(plugin);

            messages.Add(new ChatMessage
            {
                Text = $"{ChatColor.Reset}{(i + 1 < srv.PluginManager.Plugins.Count ? ", " : "")}"
            });
        }
        if (messages.Count > 0)
            message.AddExtra(messages);
        else
            message.Text = $"{ChatColor.Gold}There is no plugins installed{ChatColor.Reset}";

        await sender.SendMessageAsync(message);
    }

    [Command("save")]
    [CommandInfo("Save World", "/save")]
    public async Task SaveAsync(CommandContext Context)
    {
        var player = (Player)Context.Player;
        var world = player.World;
        await world.FlushRegionsAsync();
    }

    [Command("forcechunkreload")]
    [CommandInfo("Force chunk reload", "/forcechunkreload")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task ForceChunkReloadAsync(CommandContext Context)
    {
        var player = (Player)Context.Player;
        var c = player.client;
        var world = player.World;

        await world.UpdateClientChunksAsync(c, true);
    }

    [Command("echo")]
    [CommandInfo("Echoes given text.", "/echo <message>")]
    public void Echo(CommandContext Context, [Remaining] string text) => Context.Server.BroadcastMessage($"[{Context.Player?.Username ?? Context.Sender.ToString()}] {text}");

    [Command("announce")]
    [CommandInfo("Makes an announcement", "/announce <message>")]
    [RequirePermission(op: true, permissions: "obsidian.announce")]
    public void Announce(CommandContext Context, [Remaining] string text) => Context.Server.BroadcastMessage(text, MessageType.ActionBar);

    [Command("leave", "kickme")]
    [CommandInfo("kicks you", "/leave")]
    [IssuerScope(CommandIssuers.Client)]
    public Task LeaveAsync(CommandContext Context) => Context.Player.KickAsync("Is this what you wanted?");

    [Command("uptime", "up")]
    [CommandInfo("Gets current uptime", "/uptime")]
    public Task UptimeAsync(CommandContext Context)
        => Context.Sender.SendMessageAsync($"Uptime: {DateTimeOffset.Now.Subtract(Context.Server.StartTime)}");

    [Command("declarecmds", "declarecommands")]
    [CommandInfo("Debug command for testing the Declare Commands packet", "/declarecmds")]
    [IssuerScope(CommandIssuers.Client)]
    public Task DeclareCommandsTestAsync(CommandContext Context) => ((Player)Context.Player).client.QueuePacketAsync(Registry.DeclareCommandsPacket);

    [Command("gamemode")]
    [CommandInfo("Change your gamemode.", "/gamemode <survival/creative/adventure/spectator>")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task GamemodeAsync(CommandContext Context)
    {
        var chatMessage = SendCommandUsage("/gamemode <survival/creative/adventure/spectator>");
        var player = Context.Player;
        await player.SendMessageAsync(chatMessage);
    }

    [CommandOverload]
    [IssuerScope(CommandIssuers.Client)]
    public async Task GamemodeAsync(CommandContext Context, [Remaining] string args_)
    {
        var chatMessage = ChatMessage.Simple("");
        var args = args_.Contains(" ") ? args_.Split(" ").ToList() : new List<string> { args_ };
        if (args.Count == 1)
        {
            if (new[] { "creative", "survival", "spectator", "adventure" }.Contains(args[0].ToLowerInvariant()))
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
        var player = Context.Player;
        await player.SendMessageAsync(chatMessage);
    }

    [Command("tp")]
    [CommandInfo("teleports you to a location", "/tp <x> <y> <z>")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task TeleportAsync(CommandContext Context, [Remaining] VectorF location)
    {
        var player = Context.Player;
        await player.SendMessageAsync($"ight homie tryna tp you (and sip dicks) {location.X} {location.Y} {location.Z}");
        await player.TeleportAsync(location);
    }

    [Command("op")]
    [CommandInfo("Give operator rights to a specific player.", "/op <player>")]
    [RequirePermission]
    public async Task GiveOpAsync(CommandContext Context, IPlayer player)
    {
        if (player == null)
            return;

        Context.Server.Operators.AddOperator(player);

        await Context.Sender.SendMessageAsync($"Made {player} a server operator");
        await player.SendMessageAsync($"{(Context.IsPlayer ? Context.Player!.Username : "Console")} made you a server operator");
    }

    [Command("deop")]
    [CommandInfo("Remove specific player's operator rights.", "/deop <player>")]
    [RequirePermission]
    public async Task UnclaimOpAsync(CommandContext Context, IPlayer player)
    {
        if (player == null)
            return;

        Context.Server.Operators.RemoveOperator(player);

        await Context.Sender.SendMessageAsync($"Made {player} no longer a server operator");
        await player.SendMessageAsync($"{(Context.IsPlayer ? Context.Player!.Username : "Console")} made you no longer a server operator");

    }

    [Command("oprequest", "opreq")]
    [CommandInfo("Request operator rights.", "/oprequest [<code>]")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task RequestOpAsync(CommandContext Context, string code = null)
    {
        var server = (Server)Context.Server;
        var player = Context.Player;

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

    [Command("obsidian")]
    [CommandInfo("Shows obsidian popup", "/obsidian")]
    public async Task ObsidianAsync(CommandContext Context)
    {
        await Context.Sender.SendMessageAsync("§dWelcome to Obsidian Test Build. §l§4<3", MessageType.ActionBar);
    }

    [Command("title")]
    [CommandInfo("Sends a title", "/title")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task SendTitleAsync(CommandContext ctx)
    {
        var player = ctx.Player;

        await player.SendTitleAsync("Test Title", "Test subtitle", 20, 40, 20);
    }

    [Command("spawnentity")]
    [CommandInfo("Spawns an entity", "/spawnentity [entityType]")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task SpawnEntityAsync(CommandContext context, string entityType)
    {
        var player = context.Player;
        if (Enum.TryParse<EntityType>(entityType, true, out var type))
        {
            await player.WorldLocation.SpawnEntityAsync(player.Position, type);
            await player.SendMessageAsync($"Spawning: {type}");
        }
        else
        {
            await player.SendMessageAsync("&4Invalid entity type");
        }
    }

    [Command("stop")]
    [CommandInfo("Stops the server.", "/stop")]
    [RequirePermission(permissions: "obsidian.stop")]
    public async Task StopAsync(CommandContext Context)
    {
        var server = (Server)Context.Server;
        server.BroadcastMessage($"Server stopped by {ChatColor.Red}{Context.Player?.Username ?? Context.Sender.ToString()}{ChatColor.Reset}.");
        await Task.Run(() =>
        {
            server.Stop();
        });
    }

    [Command("time")]
    [CommandInfo("Sets declared time", "/time <timeOfDay>")]
    public async Task TimeAsync(CommandContext Context) => await TimeAsync(Context, 1337);

    [CommandOverload]
    public async Task TimeAsync(CommandContext Context, int time)
    {
        var player = Context.Player as Player;
        player.World.Data.DayTime = time;
        await Context.Player.SendMessageAsync($"Time set to {time}");
    }

    [CommandGroup("permission")]
    [RequirePermission(permissions: "obsidian.permissions")]
    public class Permission
    {
        [GroupCommand]
        [IssuerScope(CommandIssuers.Client)]
        public async Task CheckPermission(CommandContext ctx, string permission)
        {
            if (ctx.Player.HasPermission(permission))
            {
                await ctx.Player.SendMessageAsync($"You have {ChatColor.BrightGreen}{permission}{ChatColor.Reset}.");
            }
            else
            {
                await ctx.Player.SendMessageAsync($"You don't have {ChatColor.Red}{permission}{ChatColor.Reset}.");
            }
        }

        [Command("grant")]
        [IssuerScope(CommandIssuers.Client)]
        public async Task GrantPermission(CommandContext ctx, string permission)
        {
            if (await ctx.Player.GrantPermissionAsync(permission))
            {
                await ctx.Player.SendMessageAsync($"Sucessfully granted {ChatColor.BrightGreen}{permission}{ChatColor.Reset}.");
            }
            else
            {
                await ctx.Player.SendMessageAsync($"Failed granting {ChatColor.Red}{permission}{ChatColor.Reset}.");
            }
        }
        [Command("revoke")]
        [IssuerScope(CommandIssuers.Client)]
        public async Task RevokePermission(CommandContext ctx, string permission)
        {
            if (await ctx.Player.RevokePermissionAsync(permission))
            {
                await ctx.Player.SendMessageAsync($"Sucessfully revoked {ChatColor.BrightGreen}{permission}{ChatColor.Reset}.");
            }
            else
            {
                await ctx.Player.SendMessageAsync($"Failed revoking {ChatColor.Red}{permission}{ChatColor.Reset}.");
            }
        }
    }

    [Command("toggleweather", "weather")]
    [RequirePermission(permissions: "obsidian.weather")]
    public async Task WeatherAsync(CommandContext ctx)
    {
        var player = (Player)ctx.Player;
        player.World.Data.RainTime = 0;
        await ctx.Sender.SendMessageAsync("Toggled weather for this world.");
    }

    [Command("world")]
    public async Task WorldAsync(CommandContext ctx, string worldname)
    {
        var server = (Server)ctx.Server;
        if (server.WorldManager.TryGetWorldByName(worldname, out World world))
        {
            await worldSwitch(ctx, world);
            return;
        }

        if (!string.IsNullOrEmpty(worldname))
            await ctx.Player.SendMessageAsync($"No such world with name §4{worldname}§r! Try running §a/listworld§r");
    }

    [Command("listworld")]
    public async Task ListAsync(CommandContext ctx)
    {
        var server = (Server)ctx.Server;
        string available = string.Join("§r, §a", server.WorldManager.GetAvailableWorlds().Select(x => x.Name));
        await ctx.Player.SendMessageAsync($"Available worlds: §a{available}§r");
    }

    private async Task worldSwitch(CommandContext ctx, World? world)
    {
        var player = ctx.Player as Player;

        await player.SwitchWorldAsync(world);
        await ctx.Player.SendMessageAsync($"Switched to world {world.Name}.");
    }

#if DEBUG
    [Command("breakpoint")]
    [CommandInfo("Creats a breakpoint to help debug", "/breakpoint")]
    [RequirePermission(op: true)]
    public async Task BreakpointAsync(CommandContext Context)
    {
        Context.Server.BroadcastMessage("You might get kicked due to timeout, a breakpoint will hit in 3 seconds!");
        await Task.Delay(3000);
        Debugger.Break();
    }

    [Command("kaboom")]
    [CommandInfo("The big bang.")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task KaboomAsync(CommandContext Context)
    {
        var server = (Server)Context.Server;
        var player = Context.Player;
        await server.QueueBroadcastPacketAsync(new Explosion()
        {
            Position = player.Position + (10, 0, 0),
            Strength = 2.0f,
            Records = new ExplosionRecord[1] { new ExplosionRecord { X = 0, Y = 0, Z = 0 } },
            PlayerMotion = new VectorF(-10f, 0f, 0f)
        });
    }
#endif

    private ChatMessage SendCommandUsage(string commandUsage)
    {
        var commands = ChatMessage.Simple("");
        var commandSuggest = commandUsage.Contains(" ") ? $"{commandUsage.Split(" ").FirstOrDefault()} " : commandUsage;
        var usage = new ChatMessage
        {
            Text = $"{ChatColor.Red}{commandUsage}",
            ClickEvent = new ClickComponent
            (
                EClickAction.SuggestCommand,
                $"{commandSuggest}"
            ),
            HoverEvent = new HoverComponent
            (
                EHoverAction.ShowText,
                $"Click to suggest the command"
            )
        };

        var prefix = new ChatMessage
        {
            Text = $"{ChatColor.Red}Usage: "
        };

        commands.AddExtra(prefix);
        commands.AddExtra(usage);
        return commands;
    }
}
