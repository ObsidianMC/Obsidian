using Obsidian.Commands.Framework.Entities;
using Obsidian.Entities;
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
    public Task HelpAsync(CommandContext ctx) => HelpAsync(ctx, 1);

    [CommandOverload]
    public async Task HelpAsync(CommandContext ctx, int page)
    {
        var sender = ctx.Sender;
        var server = (Server)ctx.Server;
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
                if (!await check.RunChecksAsync(ctx))
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
            await sender.SendMessageAsync($"{ChatColor.Red}Invalid help page.");
            return;
        }

        var commandSection = availableCommands.Skip((page - 1) * CommandsPerPage).Take(CommandsPerPage);

        var commands = ChatMessage.Simple("\n");
        commands.AddExtra(new ChatMessage
        {
            Underlined = true,
            Text = $"List of available commands ({page}/{pagecount}):"
        });

        foreach (var cmd in commandSection.Where(x => x.Parent is null))
        {
            string usage = cmd.Usage.IsEmpty() ? $"/{cmd.Name}" : cmd.Usage;
            var commandName = new ChatMessage
            {
                Text = $"\n{ChatColor.Gold}{usage}",
                ClickEvent = new ClickComponent
                (
                    EClickAction.SuggestCommand,
                    usage.Contains(' ') ? $"{usage[..usage.IndexOf(' ')]} " : usage
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
                commands.AddExtra($"{ChatColor.Gray}:{ChatColor.Reset} {cmd.Description}");
            }
        }

        await sender.SendMessageAsync(commands);
    }

    [Command("tps")]
    [CommandInfo("Gets server TPS", "/tps")]
    public async Task TPSAsync(CommandContext ctx)
    {
        var sender = ctx.Sender;

        ChatColor color;
        if (ctx.Server.Tps > 15)
            color = ChatColor.BrightGreen;
        else if (ctx.Server.Tps > 10)
            color = ChatColor.Yellow;
        else
            color = ChatColor.Red;

        await sender.SendMessageAsync($"{ChatColor.Gold}Current server TPS: {color}{ctx.Server.Tps}");
    }

    [Command("plugins", "pl")]
    [CommandInfo("Gets all plugins", "/plugins")]
    public async Task PluginsAsync(CommandContext ctx)
    {
        var srv = (Server)ctx.Server;
        var sender = ctx.Sender;
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
    public async Task SaveAsync(CommandContext ctx)
    {
        var player = (Player)ctx.Player;
        var world = player.World;
        await world.FlushRegionsAsync();
    }

    [Command("forcechunkreload")]
    [CommandInfo("Force chunk reload", "/forcechunkreload")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task ForceChunkReloadAsync(CommandContext ctx)
    {
        var player = (Player)ctx.Player;

        await player.client.UpdateChunksAsync(true);
    }

    [Command("echo")]
    [CommandInfo("Echoes given text.", "/echo <message>")]
    public void Echo(CommandContext ctx, [Remaining] string text) => ctx.Server.BroadcastMessage($"[{ctx.Player?.Username ?? ctx.Sender.ToString()}] {text}");

    [Command("announce")]
    [CommandInfo("Makes an announcement", "/announce <message>")]
    [RequirePermission(op: true, permissions: "obsidian.announce")]
    public void Announce(CommandContext ctx, [Remaining] string text) => ctx.Server.BroadcastMessage(text, MessageType.ActionBar);

    [Command("uptime", "up")]
    [CommandInfo("Gets current uptime", "/uptime")]
    public Task UptimeAsync(CommandContext ctx)
        => ctx.Sender.SendMessageAsync($"Uptime: {DateTimeOffset.Now.Subtract(ctx.Server.StartTime)}");

    [Command("declarecmds", "declarecommands")]
    [CommandInfo("Debug command for testing the Declare Commands packet", "/declarecmds")]
    [IssuerScope(CommandIssuers.Client)]
    public Task DeclareCommandsTestAsync(CommandContext ctx) => ((Player)ctx.Player).client.QueuePacketAsync(Registry.DeclareCommandsPacket);

    [Command("gamemode")]
    [CommandInfo("Change your gamemode.", "/gamemode <survival/creative/adventure/spectator>")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task GamemodeAsync(CommandContext ctx, string gamemode)
    {
        var player = ctx.Player;

        if (!Enum.TryParse<Gamemode>(gamemode, true, out var result))
        {
            await player.SendMessageAsync(SendCommandUsage("/gamemode <survival/creative/adventure/spectator>"));
            return;
        }

        if (player.Gamemode != result)
        {
            await player.SetGamemodeAsync(result);


            await player.SendMessageAsync($"{ChatColor.Reset}Gamemode set to {ChatColor.Red}{gamemode}{ChatColor.Reset}.");

            return;
        }

        await player.SendMessageAsync($"{ChatColor.Reset}You're already in {ChatColor.Red}{gamemode}{ChatColor.Reset}.");
    }

    [Command("tp")]
    [CommandInfo("teleports you to a location", "/tp <x> <y> <z>")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task TeleportAsync(CommandContext ctx, [Remaining] VectorF location)
    {
        var player = ctx.Player;

        await player.SendMessageAsync($"Teleporting to {location.X} {location.Y} {location.Z}");
        await player.TeleportAsync(location);
    }

    [Command("op")]
    [CommandInfo("Give operator rights to a specific player.", "/op <player>")]
    [RequirePermission]
    public async Task GiveOpAsync(CommandContext ctx, IPlayer player)
    {
        if (player == null)
            return;

        ctx.Server.Operators.AddOperator(player);

        await ctx.Sender.SendMessageAsync($"Made {player} a server operator");
        await player.SendMessageAsync($"{(ctx.IsPlayer ? ctx.Player!.Username : "Console")} made you a server operator");
    }

    [Command("deop")]
    [CommandInfo("Remove specific player's operator rights.", "/deop <player>")]
    [RequirePermission]
    public async Task UnclaimOpAsync(CommandContext ctx, IPlayer player)
    {
        if (player == null)
            return;

        ctx.Server.Operators.RemoveOperator(player);

        await ctx.Sender.SendMessageAsync($"Made {player} no longer a server operator");
        await player.SendMessageAsync($"{(ctx.IsPlayer ? ctx.Player!.Username : "Console")} made you no longer a server operator");

    }

    [Command("oprequest", "opreq")]
    [CommandInfo("Request operator rights.", "/oprequest [<code>]")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task RequestOpAsync(CommandContext ctx, string code = null)
    {
        var server = (Server)ctx.Server;
        var player = ctx.Player;

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
            await player.SendMessageAsync("A request has been to the server console");

            return;
        }

        await player.SendMessageAsync("§cYou have already sent a request");
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
        if (!Enum.TryParse<EntityType>(entityType, true, out var type))
        {
            await player.SendMessageAsync("&4Invalid entity type");

            return;
        }

        await player.WorldLocation.SpawnEntityAsync(player.Position, type);
        await player.SendMessageAsync($"Spawning: {type}");
    }

    [Command("stop")]
    [CommandInfo("Stops the server.", "/stop")]
    [RequirePermission(permissions: "obsidian.stop")]
    public Task StopAsync(CommandContext ctx)
    {
        var server = (Server)ctx.Server;
        server.BroadcastMessage($"Stopping server...");

        server.Stop();

        return Task.CompletedTask;
    }

    [Command("time")]
    [CommandInfo("Sets declared time", "/time <timeOfDay>")]
    public Task TimeAsync(CommandContext ctx) => TimeAsync(ctx, 1337);

    [CommandOverload]
    public async Task TimeAsync(CommandContext ctx, int time)
    {
        var player = ctx.Player as Player;
        player.World.LevelData.DayTime = time;
        await ctx.Player.SendMessageAsync($"Time set to {time}");
    }

    [Command("toggleweather", "weather")]
    [RequirePermission(permissions: "obsidian.weather")]
    public async Task WeatherAsync(CommandContext ctx)
    {
        var player = (Player)ctx.Player;
        player.World.LevelData.RainTime = 0;
        await ctx.Sender.SendMessageAsync("Toggled weather for this world.");
    }

    [Command("world")]
    public async Task WorldAsync(CommandContext ctx, string worldname)
    {
        var server = (Server)ctx.Server;
        var player = ctx.Player;
        if (server.WorldManager.TryGetWorld(worldname, out World world))
        {
            if (player.WorldLocation.Name.EqualsIgnoreCase(worldname))
            {
                await player.SendMessageAsync("You can't switch to a world you're already in!");

                return;
            }

            await player.TeleportAsync(world);
            await player.SendMessageAsync($"Switched to world {world.Name}.");

            return;
        }

        if (!string.IsNullOrEmpty(worldname))
            await player.SendMessageAsync($"No such world with name §4{worldname}§r! Try running §a/listworld§r");
    }

    [Command("listworlds")]
    public async Task ListAsync(CommandContext ctx)
    {
        var server = (Server)ctx.Server;
        string available = string.Join("§r, §a", server.WorldManager.GetAvailableWorlds().Select(x => x.Name));
        await ctx.Player.SendMessageAsync($"Available worlds: §a{available}§r");
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
#endif

    private ChatMessage SendCommandUsage(string commandUsage)
    {
        var commands = ChatMessage.Simple("");
        var commandSuggest = commandUsage.Contains(' ') ? $"{commandUsage.Split(" ").FirstOrDefault()} " : commandUsage;
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
