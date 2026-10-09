using Obsidian.API.Commands;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
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
        var commandHandler = this.Server.CommandHandler;
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
                Text = $"\n{usage}",
                ClickEvent = new ClickComponent
                {
                    Action = ClickAction.SuggestCommand,
                    Value = usage.Contains(' ') ? $"{usage[..usage.IndexOf(' ')]} " : usage
                },
                HoverEvent = new HoverComponent
                {
                    Action = HoverAction.ShowText,
                    Contents = new HoverChatContent() { ChatMessage = "Click to suggest the command" }
                },
                Color = HexColor.Gold
            };
            commands.AddExtra(commandName);

            if (!cmd.Description.IsNullOrEmpty())
            {
                var desc = ChatMessage.Simple(": ");

                desc.AddExtra(ChatMessage.Simple(cmd.Description) with { Color = HexColor.White });

                commands.AddExtra(desc);
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
        if (this.Player?.Level is World world)
        {
            await world.FlushAsync();
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
            await player.Client.QueuePacketAsync(CommandsRegistry.Packet);
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
            player.Client.SendPacket(new ContainerSetSlotPacket
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

    [Command("clearinventory")]
    [CommandInfo("Clears your inventory, armor, offhand, and cursor.", "/clearinventory")]
    [IssuerScope(CommandIssuers.Client)]
    [RequirePermission(op: true, permissions: "obsidian.clearinventory")]
    public async Task ClearInventoryAsync()
    {
        if (this.Player is not Player player)
            return;

        player.CancelEating();
        player.CancelWeaponUse();
        for (var slot = 0; slot < player.Inventory.Size; slot++)
            player.Inventory.SetItem(slot, null);
        player.CarriedItem = null;
        player.DraggedSlots.Clear();

        await player.Client.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList())
        {
            StateId = player.Inventory.StateId++,
            CarriedItem = null
        });
        if (player.OpenedContainer is { } container)
        {
            await player.Client.QueuePacketAsync(new ContainerSetContentPacket(player.CurrentContainerId,
                container.Concat(player.Inventory.Skip(9).Take(36)).ToList())
            {
                StateId = player.Inventory.StateId++,
                CarriedItem = null
            });
        }
        await player.SendMessageAsync("Inventory cleared.");
    }

    [Command("gamemode")]
    [CommandInfo("Change your gamemode.", "/gamemode <survival/creative/adventure/spectator>")]
    [IssuerScope(CommandIssuers.Client)]
    [RequirePermission(op: true, permissions: "obsidian.gamemode")]
    public async Task GamemodeAsync(string gamemode)
    {
        if (this.Player is not Player player)
            return;

        // TryParse accepts any number, so "/gamemode 4" needs the defined check.
        if (!Enum.TryParse<GameMode>(gamemode, true, out var result) || !Enum.IsDefined(result))
        {
            await player.SendMessageAsync(SendCommandUsage("/gamemode <survival/creative/adventure/spectator>"));
            return;
        }

        if (player.GameMode != result)
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
    public async Task TeleportAsync([Remaining] VectorD location)
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
        if (player is null)
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
        if (player is null)
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

        var builder = player.Level.GetNewEntitySpawner()
            .WithEntityType(type)
            .AtPosition(player.Position)
            .Spawn();

        await player.SendMessageAsync($"Spawning: {type}");
        if (builder is Mob mob)
            await SendMobStatusAsync(player, mob);
    }

    [Command("mobinfo")]
    [CommandInfo("Shows nearby mob ticking and save eligibility", "/mobinfo")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task MobInfoAsync()
    {
        if (Player is not IPlayer player)
            return;
        var mobs = player.Level.GetNonPlayerEntitiesInRange(player.Position, 32).OfType<Mob>()
            .OrderBy(mob => (mob.Position - player.Position).MagnitudeSquared()).Take(5).ToArray();
        if (mobs.Length == 0)
            await player.SendMessageAsync("No registered mobs within 32 blocks.");
        foreach (var mob in mobs)
            await SendMobStatusAsync(player, mob);
    }

    private async Task SendMobStatusAsync(IPlayer player, Mob mob)
    {
        var level = mob.Level as AbstractLevel;
        var (x, z) = mob.Position.ToChunkCoord();
        var registered = level?.GetRegionForLocation(mob.Position)?.Entities.ContainsKey(mob.EntityId) == true;
        await player.SendMessageAsync($"{mob.Type} ({mob.GetType().Name}) id={mob.EntityId}: AI={mob.HasAi}, ticks={mob.AiTick}, " +
            $"health={mob.Health}, registered={registered}, chunkLoaded={level?.GetLoadedChunk(x, z) != null}, " +
            $"ticking={level?.IsMobTicking(mob.Position)}, noAI={mob.MobBitMask.HasFlag(MobBitmask.NoAi)}, " +
            $"saveEligible={mob.HasAi && mob.Alive && !mob.IsRemoved}, worldReady={Server.WorldManager.ReadyToJoin}, time={mob.Level.Time}");
        await player.SendMessageAsync($"Server tick: {(Server as Obsidian.Server)?.TickStage}; level tick: {level?.TickStage}; saving entities: {level?.SavingEntities}");
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

        var frogge = player.Level.GetNewEntitySpawner()
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
            player.Level.LevelData.RainTime = 0;
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
            if (player.Level.Name.EqualsIgnoreCase(worldname))
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

    [Command("hunger")]
    [CommandInfo("Sets your hunger level.", "/hunger <0-20>")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task HungerAsync(int hunger)
    {
        if (Player is not Player player) return;
        if (hunger is < 0 or > 20)
        {
            await player.SendMessageAsync(SendCommandUsage("/hunger <0-20>"));
            return;
        }
        player.FoodLevel = hunger;
        player.FoodSaturationLevel = Math.Min(player.FoodSaturationLevel, hunger);
        player.FoodExhaustionLevel = 0;
        player.FoodTickTimer = 0;
        await player.Client.QueuePacketAsync(new SetHealthPacket(player.Health, hunger, player.FoodSaturationLevel));
        await player.SendMessageAsync($"Hunger set to {hunger}.");
    }

    [Command("health")]
    [CommandInfo("Sets your health.", "/health <0-20>")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task HealthAsync(float health)
    {
        if (Player is not Player player) return;
        if (!float.IsFinite(health) || health is < 0 or > 20)
        {
            await player.SendMessageAsync(SendCommandUsage("/health <0-20>"));
            return;
        }
        if (!player.Alive)
        {
            await player.SendMessageAsync("Respawn before setting health.");
            return;
        }
        if (health == 0) await player.KillAsync(player, ChatMessage.Simple("You died."));
        else
        {
            player.Health = health;
            await player.Client.QueuePacketAsync(new SetHealthPacket(health, player.FoodLevel, player.FoodSaturationLevel));
            player.Level.PacketBroadcaster.QueuePacketToLevel(player.Level, new SetEntityDataPacket { EntityId = player.EntityId, Entity = player });
        }
        await player.SendMessageAsync($"Health set to {health}.");
    }

    [Command("dimension")]
    [CommandInfo("Transfers you to another dimension.", "/dimension <overworld|nether|end>")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task DimensionAsync(string dimension)
    {
        if (Player is not Player player) return;
        var name = dimension.ToLowerInvariant() switch
        {
            "overworld" => "minecraft:overworld",
            "nether" => "minecraft:the_nether",
            "end" => "minecraft:the_end",
            _ => null
        };
        if (name == null)
        {
            await player.SendMessageAsync(SendCommandUsage("/dimension <overworld|nether|end>"));
            return;
        }
        var world = player.Level is IDimension child ? child.ParentWorld as World : player.Level as World;
        ILevel? destination = world?.DimensionName == name ? world : world?.dimensions.GetValueOrDefault(name);
        if (destination == null)
        {
            await player.SendMessageAsync("That dimension is not available in this world.");
            return;
        }
        if (!player.Alive || player.Respawning)
        {
            await player.SendMessageAsync("Respawn before changing dimensions.");
            return;
        }
        await player.TransferDimensionAsync(destination);
        await player.SendMessageAsync($"Transferred to {dimension.ToLowerInvariant()}.");
    }

    [Command("spawn")]
    [CommandInfo("Teleports you to this dimension's spawn.", "/spawn")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task SpawnAsync()
    {
        if (Player is not Player player || !player.Alive) return;
        await player.TeleportAsync(player.Level.LevelData.SpawnPosition);
        await player.SendMessageAsync("Teleported to spawn.");
    }

    [Command("setspawn")]
    [CommandInfo("Sets this dimension's spawn to your position.", "/setspawn")]
    [IssuerScope(CommandIssuers.Client)]
    public async Task SetSpawnAsync()
    {
        if (Player is not Player player || !player.Alive) return;
        var block = (Vector)player.Position.Floor();
        player.Level.LevelData.SpawnPosition = (VectorD)block + new VectorD(0.5, 0, 0.5);
        await player.Level.SaveAsync();
        player.Level.PacketBroadcaster.QueuePacketToLevel(player.Level,
            new SetDefaultSpawnPositionPacket(new() { Dimension = player.Level.DimensionName, Pos = block }, 0, 0));
        await player.SendMessageAsync($"Spawn set to {block.X} {block.Y} {block.Z}.");
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

    private static ChatMessage SendCommandUsage(string commandUsage)
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
