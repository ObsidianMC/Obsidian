using Obsidian.API.Commands;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.WorldData;
using System.Diagnostics;

namespace Obsidian.Commands.Modules;


public sealed class MainCommandModule : CommandModuleBase
{
    private const int CommandsPerPage = 15;

    [Command("help", "commands")]
    [CommandInfo("Lists available commands.", "/help [<page>]")]
    public Task HelpAsync() => HelpAsync(1);

    [CommandOverload]
    public async Task HelpAsync(int page)
    {
        var sender = this.Sender;
        var server = (Server)this.Server;
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
                if (!await check.RunChecksAsync(this.CommandContext))
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
            string usage = cmd.Usage.IsNullOrEmpty() ? $"/{cmd.Name}" : cmd.Usage;
            var commandName = new ChatMessage
            {
                Text = $"\n{ChatColor.Gold}{usage}",
                ClickEvent = new ClickComponent
                {
                    Action = ClickAction.SuggestCommand,
                    Value = usage.Contains(' ') ? $"{usage[..usage.IndexOf(' ')]} " : usage
                },
                HoverEvent = new HoverComponent
                {
                    Action = HoverAction.ShowText,
                    Contents = new HoverChatContent() { ChatMessage = "Click to suggest the command" }
                }
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
    public async Task TPSAsync()
    {
        var sender = this.Sender;

        ChatColor color = this.Server.Tps switch
        {
            > 15 => ChatColor.BrightGreen,
            > 10 => ChatColor.Yellow,
            _ => ChatColor.Red
        };

        await sender.SendMessageAsync($"{ChatColor.Gold}Current server TPS: {color}{this.Server.Tps}");
    }

    [Command("plugins", "pl")]
    [CommandInfo("Gets all plugins", "/plugins")]
    public async Task PluginsAsync()
    {
        var srv = (Server)this.Server;
        var sender = this.Sender;
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
            var colorByState = pluginContainer.Loaded ? ChatColor.BrightGreen : ChatColor.Red;

            plugin.Text = pluginContainer.Info.Name;
            plugin.Color = new HexColor(colorByState.Color);

            plugin.HoverEvent = new HoverComponent
            {
                Action = HoverAction.ShowText,
                Contents = new HoverChatContent { ChatMessage = $"{colorByState}{info.Name}{ChatColor.Reset}\nVersion: {colorByState}{info.Version}{ChatColor.Reset}\nAuthor(s): {colorByState}{string.Join(", ", info.Authors)}{ChatColor.Reset}" }
            };

            if (pluginContainer.Info.ProjectUrl != null)
                plugin.ClickEvent = new ClickComponent { Action = ClickAction.OpenUrl, Value = pluginContainer.Info.ProjectUrl.AbsoluteUri };

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
    public async Task SaveAsync()
    {
        if (this.Player?.World is World world)
        {
            await world.FlushRegionsAsync();
        }
    }

    [Command("forcechunkreload")]
    [CommandInfo("Force chunk reload", "/forcechunkreload")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task ForceChunkReloadAsync()
    {
        if (this.Player is Player player)
        {
            await player.UpdateChunksAsync(true);
        }
    }

    [Command("echo")]
    [CommandInfo("Echoes given text.", "/echo <message>")]
    public void Echo([Remaining] string text) => this.Server.BroadcastMessage($"[{this.Player?.Username ?? this.Sender.ToString()}] {text}");

    [Command("announce")]
    [CommandInfo("Makes an announcement", "/announce <message>")]
    [RequirePermission(op: true, permissions: "obsidian.announce")]
    public void Announce([Remaining] string text) => this.Server.BroadcastMessage(text);

    [Command("uptime", "up")]
    [CommandInfo("Gets current uptime", "/uptime")]
    public Task UptimeAsync()
        => this.Sender.SendMessageAsync($"Uptime: {DateTimeOffset.Now.Subtract(this.Server.StartTime)}");

    [Command("declarecmds", "declarecommands")]
    [CommandInfo("Debug command for testing the Declare Commands packet", "/declarecmds")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task DeclareCommandsTestAsync()
    {
        if (this.Player is Player player)
        {
            await player.client.QueuePacketAsync(CommandsRegistry.Packet);
        }
    }

    [Command("give")]
    [CommandInfo("Gives you a block or item", "/get <item> <amount>")]
    [IssuerScope(CommandIssuers.Client)]
    [RequirePermission(op: true, permissions: "obsidian.give")]
    public Task GiveAsync(string item) => GiveAsync(item, 64);

    [CommandOverload]
    public async Task GiveAsync(string item, int amount = 64)
    {
        if (this.Player is not Player player)
            return;

        // convert snake_case to PascalCase
        if (item.Contains('_'))
        {
            var parts = item.Split('_');
            item = string.Join("", parts.Select(x => $"{x[0].ToString().ToUpperInvariant()}{x.Substring(1)}"));
        }
        // find material from string (enum Material)
        if (Enum.TryParse(item, out Material material))
        {
            var slot = player.Inventory.AddItem(new ItemStack(ItemsRegistry.Get(material), count: amount));
            await player.SendMessageAsync($"Given you {ChatColor.Gold}{amount} {item}(s)");
            player.client.SendPacket(new ContainerSetSlotPacket
            {
                Slot = (short)slot,
                ContainerId = 0,
                SlotData = player.Inventory.GetItem(slot)!,
                StateId = player.Inventory.StateId++
            });
        }
        else
        {
            await player.SendMessageAsync($"{ChatColor.Red}Invalid item: {item}");
        }
    }

    [Command("gamemode")]
    [CommandInfo("Change your gamemode.", "/gamemode <survival/creative/adventure/spectator>")]
    [IssuerScope(CommandIssuers.Client)]
    [RequirePermission(op: true, permissions: "obsidian.gamemode")]
    public async Task GamemodeAsync(string gamemode)
    {
        if (this.Player is not Player player)
            return;

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
    public async Task TeleportAsync([Remaining] VectorF location)
    {
        if (this.Player is not IPlayer player)
            return;

        await player.SendMessageAsync($"Teleporting to {location.X} {location.Y} {location.Z}");
        await player.TeleportAsync(location);
    }

    [Command("op")]
    [CommandInfo("Give operator rights to a specific player.", "/op <player>")]
    [RequirePermission]
    public async Task GiveOpAsync(IPlayer player)
    {
        if (player == null)
            return;

        this.Server.Operators.AddOperator(player);

        await this.Sender.SendMessageAsync($"Made {player} a server operator");
        await player.SendMessageAsync($"{(this.IsPlayer ? this.Player!.Username : "Console")} made you a server operator");
    }

    [Command("deop")]
    [CommandInfo("Remove specific player's operator rights.", "/deop <player>")]
    [RequirePermission]
    public async Task UnclaimOpAsync(IPlayer player)
    {
        if (player == null)
            return;

        this.Server.Operators.RemoveOperator(player);

        await this.Sender.SendMessageAsync($"Made {player} no longer a server operator");
        await player.SendMessageAsync($"{(this.IsPlayer ? this.Player!.Username : "Console")} made you no longer a server operator");

    }

    [Command("oprequest", "opreq")]
    [CommandInfo("Request operator rights.", "/oprequest [<code>]")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task RequestOpAsync(string? code = null)
    {
        if (this.Server is not Server server || this.Player is not IPlayer player)
            return;

        if (!server.Configuration.AllowOperatorRequests)
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
    public async Task SendTitleAsync()
    {
        if (this.Player is IPlayer player)
        {
            await player.SendTitleAsync("Test Title", "Test subtitle", 20, 40, 20);
        }
    }

    [Command("spawnentity")]
    [CommandInfo("Spawns an entity", "/spawnentity [entityType]")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task SpawnEntityAsync(string entityType)
    {
        if (this.Player is not IPlayer player)
            return;

        if (!Enum.TryParse<EntityType>(entityType, true, out var type))
        {
            await player.SendMessageAsync("&4Invalid entity type");
            return;
        }

        var builder = player.World.GetNewEntitySpawner()
            .WithEntityType(type)
            .AtPosition(player.Position)
            .Spawn();

        await player.SendMessageAsync($"Spawning: {type}");
    }

    [Command("derp")]
    [CommandInfo("derpy derp spawns a derp", "/derp [entity_type]")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task DerpAsync(string entityType)
    {
        // I was bored
        if (this.Player is not IPlayer player)
            return;

        if (!Enum.TryParse<EntityType>(entityType, true, out var type))
        {
            await player.SendMessageAsync("&4Invalid entity type");
            return;
        }

        var frogge = player.World.GetNewEntitySpawner()
            .WithEntityType(type)
            .AtPosition(player.Position)
            .WithCustomName("Derpy Derp")
            .AsBaby()
            .IsBurning()
            .IsGlowing()
            .WithAbsorbedArrows(50)
            .WithAbsorbedStingers(50)
            .WithAmbientPotionEffect(true)
            .Spawn();
        var server = (this.Server as Server)!;

        _ = Task.Run(async () =>
        {
            while (true)
            {
                frogge.SetHeadRotation(new Angle((byte)(Random.Shared.Next(1, 255))));
                frogge.SetRotation(new Angle((byte)(Random.Shared.Next(1, 255))), new Angle((byte)(Random.Shared.Next(1, 255))), MovementFlags.None);

                await Task.Delay(15);
            }
        });

        server.BroadcastMessage(ChatMessage.Simple($"Spawned with entity ID {frogge.EntityId}"));
    }

    [Command("stop")]
    [CommandInfo("Stops the server.", "/stop")]
    [RequirePermission(permissions: "obsidian.stop")]
    public async Task StopAsync()
    {
        var server = (Server)this.Server;
        server.BroadcastMessage($"Stopping server...");

        await server.StopAsync();
    }

    [Command("toggleweather", "weather")]
    [RequirePermission(permissions: "obsidian.weather")]
    public async Task WeatherAsync()
    {
        if (this.Player is Player player)
        {
            player.world.LevelData.RainTime = 0;
            await this.Sender.SendMessageAsync("Toggled weather for this world.");
        }
    }

    [Command("world")]
    public async Task WorldAsync(string worldname)
    {
        if (this.Server is not Server server || this.Player is not IPlayer player)
            return;

        if (server.WorldManager.TryGetWorld(worldname, out World? world))
        {
            if (player.World.Name.EqualsIgnoreCase(worldname))
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
    public async Task ListAsync()
    {
        if (this.Server is not Server server)
            return;

        string available = string.Join("§r, §a", server.WorldManager.GetAvailableWorlds().Select(x => x.Name));
        await this.Sender.SendMessageAsync($"Available worlds: §a{available}§r");
    }

#if DEBUG
    [Command("breakpoint")]
    [CommandInfo("Creats a breakpoint to help debug", "/breakpoint")]
    [RequirePermission(op: true)]
    public async Task BreakpointAsync()
    {
        this.Server.BroadcastMessage("You might get kicked due to timeout, a breakpoint will hit in 3 seconds!");
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
            {
                Action = ClickAction.SuggestCommand,
                Value = commandSuggest
            },
            HoverEvent = new HoverComponent
            {
                Action = HoverAction.ShowText,
                Contents = new HoverChatContent { ChatMessage = "Click to suggest the command" }
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
}
