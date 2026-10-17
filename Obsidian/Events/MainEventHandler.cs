using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Events;

public sealed partial class MainEventHandler : MinecraftEventHandler
{
    [EventPriority(Priority = Priority.Internal)]
    public Task OnIncomingChatMessage(IncomingChatMessageEventArgs e)
    {
        if (e.IsCancelled)
            return Task.CompletedTask;

        var server = e.Server;

        //TODO add bool for sending secure chat messages
        ChatColor nameColor = e.Server.Operators.IsOperator(e.Player) ? ChatColor.BrightGreen : ChatColor.Gray;
        var message = ChatMessage.Simple(e.Player.Username, nameColor).AppendText($": {e.Message}", ChatColor.White);
        server.BroadcastMessage(message);

        return Task.CompletedTask;
    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task PlayerAttack(PlayerAttackEntityEventArgs e)
    {
        if (e.IsCancelled)
            return;

        var entity = e.Entity;
        var attacker = e.Attacker;

        if (entity is IPlayer || entity is Mob { HasAi: true } || entity is EndCrystal or EnderDragonPart || entity is MobProjectile { Type: EntityType.Fireball })
        {
            async ValueTask Damage()
            {
                if (entity.Level == attacker.Level && (attacker is PlayerBase rangePlayer ? rangePlayer.CanAttack(entity, e.Weapon) : entity.IsInRange(attacker, 4)) &&
                    (entity is not Mob mob || mob.CanSee(attacker)))
                {
                    if (attacker is PlayerBase attackingPlayer) await attackingPlayer.AttackAsync(e);
                    else await entity.DamageAsync(attacker, e.Damage > 0 ? e.Damage : 1);
                    if (entity is Living && attacker is IPlayer owner)
                        Wolf.AlertOwnedWolves(owner, entity);
                }
            }

            if (entity.Level is Obsidian.WorldData.AbstractLevel level)
                level.EnqueueEntityAction(Damage);
            else
                await Damage();
        }
    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task OnEntityInteract(EntityInteractEventArgs e)
    {
        if (e.IsCancelled || e.TargetPosition != null)
            return;

        // The entity's interaction and the item's use run on the entity's level tick, like the entity's own tick.
        ValueTask Interact() => InteractWithEntityAsync(e.Player, e.Entity, e.Hand ?? InteractionHand.MainHand);
        if (e.Entity.Level is Obsidian.WorldData.AbstractLevel level)
            level.EnqueueEntityAction(Interact);
        else
            await Interact();
    }

    /// <summary>
    /// A player's use of an entity (right-clicking it) with the item in <paramref name="hand"/>.
    /// </summary>
    internal static async ValueTask InteractWithEntityAsync(IPlayer player, IEntity entity, InteractionHand hand)
    {
        // Like vanilla's Player.interactOn, the entity's own interaction goes first, and the held item is used on a
        // living entity only when the entity didn't use the interaction. A plugin's projectile or object does what
        // its type's behaviour does, like a mob.
        var used = entity switch
        {
            PluginEntity pluginEntity => await pluginEntity.InteractAsync(player, hand),
            Mob { HasAi: true } mob => await mob.InteractAsync(player, hand),
            _ => false
        };

        if (!used && entity is ILiving living)
            await Plugins.ItemBehaviors.UseOnEntityAsync(player, hand, living);
    }

    //TODO fix sounds
    [EventPriority(Priority = Priority.Internal)]
    public async Task OnContainerClosed(ContainerClosedEventArgs e)
    {
        if (e.IsCancelled)
            return;

        var player = (e.Player as PlayerBase)!;

        if (player.Alive && e.Container is CraftingTable { Type: InventoryType.Crafting })
            await ReturnCraftingItemsAsync(player, e.Container, 3);
        if (player.Alive && e.Container is EnchantmentTable enchantingTable)
            await ReturnEnchantingItemsAsync(player, enchantingTable);

        // Only the window that is closing: a plugin's handler may have opened another one in its place.
        if (ReferenceEquals(player.OpenedContainer, e.Container) && player.CurrentContainerId == e.WindowId)
            player.OpenedContainer = null;

        player.RunMenusClosed();

        // A double chest's halves both close.
        BaseContainer[] parts = e.Container is DoubleChestContainer chest
            ? [chest.First, chest.Second]
            : [e.Container];

        foreach (var part in parts.OfType<Container>())
            await AnimateCloseAsync(player, part.BlockPosition);
    }

    private static async ValueTask AnimateCloseAsync(PlayerBase player, Vector position)
    {
        var block = await player.Level.GetBlockAsync(position);

        if (block is null)
            return;

        switch (block.RegistryId)
        {
            case BlockIds.Chest:
            case BlockIds.TrappedChest:
                {
                    await player.QueuePacketAsync(new BlockEventPacket
                    {
                        Position = position,
                        ActionId = 1,
                        ActionParam = 0,
                        BlockType = block.BaseId
                    });

                    //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockChestClose)
                    //    .WithSoundPosition(position.SoundPosition)
                    //    .Build());

                    break;
                }
            case BlockIds.EnderChest:
                {
                    await player.QueuePacketAsync(new BlockEventPacket
                    {
                        Position = position,
                        ActionId = 1,
                        ActionParam = 0,
                        BlockType = block.BaseId
                    });

                    //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockEnderChestClose)
                    //    .WithSoundPosition(position.SoundPosition)
                    //    .Build());
                    break;
                }
            case BlockIds.Barrel://Barrels don't have a block action
                {
                    //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockBarrelClose)
                    //    .WithSoundPosition(position.SoundPosition)
                    //    .Build());

                    break;
                }
            case BlockIds.ShulkerBox:
                {
                    await player.QueuePacketAsync(new BlockEventPacket
                    {
                        Position = position,
                        ActionId = 1,
                        ActionParam = 0,
                        BlockType = block.BaseId
                    });

                    //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockShulkerBoxClose)
                    //    .WithSoundPosition(position.SoundPosition)
                    //    .Build());

                    break;
                }
        }
    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task OnPlayerInteract(PlayerInteractEventArgs e)
    {
        if (e.IsCancelled)
            return;

        var item = e.Item;

        var block = e.Block;
        var server = e.Server as Server;
        var player = e.Player as PlayerBase;

        if (block == null && player != null && item is { Count: > 0 } &&
            (player.Vehicle is Pig && item.Is(ItemsRegistry.CarrotOnAStick)
                || player.Vehicle is Strider && item.Is(ItemsRegistry.WarpedFungusOnAStick)))
        {
            async ValueTask Boost()
            {
                var boosted = player.Vehicle switch { Pig pig => pig.Boost(player), Strider strider => strider.Boost(player), _ => false };
                if (!boosted || player.GameMode == GameMode.Creative)
                    return;
                var component = Obsidian.API.Inventory.DataComponents.ComponentBuilder.Damage with
                {
                    Value = item.Damage + (player.Vehicle is Strider ? 1 : 7)
                };
                item[DataComponentType.Damage] = component;
                var slot = e.Hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
                if (component.Value >= (player.Vehicle is Strider ? 100 : 25))
                    player.Inventory.SetItem(slot, ItemsRegistry.GetSingleItem(ItemsRegistry.FishingRod));
                await player.QueuePacketAsync(new ContainerSetSlotPacket
                {
                    ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot)
                });
            }
            if (player.Level is Obsidian.WorldData.AbstractLevel level)
                level.EnqueueEntityAction(Boost);
            else
                await Boost();
            return;
        }

        if (e.IsCancelled)
            return;

        if (block is not null)
        {
            if (e.BlockLocation is not Vector blockPosition)
                return;

            player.LastClickedBlock = block;

            var type = block.RegistryId;

            BaseContainer? container = type switch
            {
                BlockIds.Anvil or BlockIds.SmithingTable => new AnvilContainer(
                    type == BlockIds.Anvil ? "anvil" : "smithing_table")
                {
                    Title = type == BlockIds.Anvil ? "Anvil" : "Smithing Table"
                },
                BlockIds.EnchantingTable => new EnchantmentTable
                {
                    BlockPosition = blockPosition
                },
                BlockIds.Dropper or BlockIds.Dispenser => new Container(9)
                {
                    Owner = player.Uuid,
                    Title = type == BlockIds.Dropper ? "Dropper" : "Dispenser",
                    BlockPosition = blockPosition,
                    Id = type is BlockIds.Dropper ? "dropper" : "dispenser"
                },
                BlockIds.BrewingStand => new BrewingStand
                {
                    BlockPosition = blockPosition
                },
                BlockIds.Hopper => new Container(5)
                {
                    BlockPosition = blockPosition
                },
                BlockIds.CraftingTable => new CraftingTable(),
                BlockIds.Loom => new Loom(),
                BlockIds.CartographyTable => new CartographyTable(),
                BlockIds.Stonecutter => new Stonecutter(),
                BlockIds.Grindstone => new Grindstone(),

                _ => null
            };
            //TODO check if container is cached if so get that container
            if (type is BlockIds.Chest or BlockIds.TrappedChest)
            {
                container = NewChest(player, blockPosition);

                // The inventory opens below, once the chest's contents (and loot) are in place.
                await player.QueuePacketAsync(new BlockEventPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockChestOpen, SoundCategory.Blocks)
                //    .WithSoundPosition(blockPosition.SoundPosition)
                //    .Build());
            }
            else if (type == BlockIds.EnderChest)
            {
                container = new Container
                {
                    Owner = player.Uuid,
                    Title = "Ender Chest",
                    Id = "ender_chest"
                };

                await player.OpenInventoryAsync(container);
                await player.QueuePacketAsync(new BlockEventPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockEnderChestOpen, SoundCategory.Blocks)
                //    .WithSoundPosition(blockPosition.SoundPosition)
                //    .Build());
            }
            else if (type is BlockIds.Furnace or BlockIds.BlastFurnace or BlockIds.Smoker)
            {
                InventoryType actualType = type switch
                {
                    BlockIds.Furnace => InventoryType.Furnace,
                    BlockIds.BlastFurnace => InventoryType.BlastFurnace,
                    BlockIds.Smoker => InventoryType.Smoker,
                    _ => InventoryType.Furnace
                };

                container = new SmeltingContainer(actualType, actualType.ToString().ToSnakeCase())
                {
                    BlockPosition = blockPosition,
                    Title = actualType.ToString()
                };
            }
            else if (type is BlockIds.ShulkerBox) //TODO colored shulker boxes as well
            {
                container = new Container // TODO shulker box functionality
                {
                    Owner = player.Uuid,
                    Title = "Shulker Box",
                    BlockPosition = blockPosition,
                    Id = "shulker_box"
                };

                await player.QueuePacketAsync(new BlockEventPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockShulkerBoxOpen, SoundCategory.Blocks)
                //    .WithSoundPosition(blockPosition.SoundPosition)
                //    .Build());
            }
            else if (type == BlockIds.Barrel)
            {
                container = new Container
                {
                    //Owner = player.Uuid,
                    Title = "Barrel",
                    BlockPosition = blockPosition,
                    Id = "Barrel"
                };
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockBarrelOpen, SoundCategory.Blocks)
                //    .WithSoundPosition(blockPosition.SoundPosition)
                //    .Build());
            }
            else if (type == BlockIds.Lectern)
            {
                //TODO open lectern??
            }

            if (container is not null && player.Level is AbstractLevel level)
            {
                container = await StoredContainerAsync(player, level, blockPosition, container);

                if (type is BlockIds.Chest or BlockIds.TrappedChest)
                    container = await DoubleChestAsync(player, level, block, blockPosition, container);
            }

            await player.OpenInventoryAsync(container);
        }
        else
        {
            //TODO check for other
        }
    }

    private static Container NewChest(PlayerBase player, Vector position) => new()
    {
        Owner = player.Uuid,
        Title = "Chest",
        BlockPosition = position,
        Id = "chest"
    };

    /// <summary>
    /// The inventory of the container block entity at <paramref name="position"/>, filled from the loot table it was
    /// waiting for, if any; or <paramref name="fresh"/> for blocks without one, whose menus each player gets their
    /// own of (anvils, enchanting tables).
    /// </summary>
    private static async ValueTask<BaseContainer> StoredContainerAsync(
        PlayerBase player,
        AbstractLevel level,
        Vector position,
        BaseContainer fresh)
    {
        if (await level.GetOrCreateBlockEntityAsync(position) is not ContainerBlockEntity stored)
            return fresh;

        level.UnpackLootTable(stored, player);

        // The stored inventory itself is opened, so changes reach the block entity that's saved with the chunk.
        return stored.Inventory;
    }

    /// <summary>
    /// Joins a chest's container with its other half's, when the chest is half of a double chest, like vanilla: the
    /// neighbour its type and facing point to must be the same block, facing the same way, with the other type. The
    /// half whose type is <c>right</c> comes first.
    /// </summary>
    /// <returns>The double chest's container, or <paramref name="container"/> for a single chest.</returns>
    private static async ValueTask<BaseContainer> DoubleChestAsync(
        PlayerBase player,
        AbstractLevel level,
        IBlock block,
        Vector position,
        BaseContainer container)
    {
        var chestType = block.GetProperty("type");
        var facing = block.GetProperty("facing");
        if (chestType is not ("left" or "right") || ConnectedOffset(chestType, facing) is not Vector offset)
            return container;

        var neighbourPosition = position + offset;
        var neighbour = await level.GetBlockAsync(neighbourPosition);
        var neighbourType = neighbour?.GetProperty("type");

        if (neighbour is null
            || neighbour.RegistryId != block.RegistryId
            || neighbour.GetProperty("facing") != facing
            || neighbourType is not ("left" or "right")
            || neighbourType == chestType)
        {
            return container;
        }

        var other = await StoredContainerAsync(player, level, neighbourPosition, NewChest(player, neighbourPosition));

        await player.QueuePacketAsync(new BlockEventPacket
        {
            Position = neighbourPosition,
            ActionId = 1,
            ActionParam = 1,
            BlockType = neighbour.RegistryId
        });

        var (first, second) = chestType == "right" ? (container, other) : (other, container);
        var title = first.CustomName ?? second.CustomName ?? "Large Chest";

        return new DoubleChestContainer(first, second, title);
    }

    // Where a half of a double chest finds its other half: a left half clockwise of its facing, a right half
    // counter-clockwise, seen from above, like vanilla's ChestBlock.getConnectedDirection.
    private static Vector? ConnectedOffset(string chestType, string? facing)
    {
        string[] clockwise = ["north", "east", "south", "west"];
        var index = Array.IndexOf(clockwise, facing);
        if (index < 0)
            return null;

        var turn = chestType == "left" ? 1 : 3;

        return clockwise[(index + turn) % 4] switch
        {
            "north" => new Vector(0, 0, -1),
            "east" => new Vector(1, 0, 0),
            "south" => new Vector(0, 0, 1),
            _ => new Vector(-1, 0, 0)
        };
    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task OnPlayerLeave(PlayerLeaveEventArgs e)
    {
        var player = e.Player;
        var server = e.Server;

        if (player.OpenedContainer is CraftingTable { Type: InventoryType.Crafting } table)
            await ReturnCraftingItemsAsync(player, table, 3);
        if (player.OpenedContainer is EnchantmentTable enchantingTable)
            await ReturnEnchantingItemsAsync(player, enchantingTable);
        await ReturnCraftingItemsAsync(player, player.Inventory, 2);

        if (player is PlayerBase portalPlayer)
            await portalPlayer.CancelPortalTravelAsync();

        API.Data.AttachmentStorage.For(player).Retire();
        await player.SaveAsync();

        await DespawnPlayerAsync(player);

        server.BroadcastMessage(string.Format(server.Configuration.Messages.Leave, e.Player.Username));
    }

    /// <summary>
    /// Takes a leaving player out of every client's view: the tab list entry and the spawned entity.
    /// Also used to undo a join that failed part-way, so it must tolerate a player who was never fully shown.
    /// </summary>
    internal static async ValueTask DespawnPlayerAsync(IPlayer player)
    {
        player.Level.PacketBroadcaster.Broadcast(new PlayerInfoRemovePacket
        {
            UUIDs = [player.Uuid]
        }, player.EntityId);

        // Like changing level or dying, leaving removes the player's entity for everyone who could see it.
        await player.Level.DestroyEntityAsync(player);
        foreach (var observer in player.Level.GetPlayersInRange(player.Position, float.MaxValue))
            observer.ForgetVisiblePlayer(player);
    }

    [EventPriority(Priority = Priority.Internal)]
    public ValueTask OnPlayerJoin(PlayerJoinEventArgs e)
    {
        var joined = e.Player;
        var server = e.Server;

        var packetBroadcaster = joined.Level.PacketBroadcaster;

        if (!joined.Level.TryAddPlayer(joined))
            throw new InvalidOperationException($"Player '{joined.Username}' is already registered in level '{joined.Level.Name}'.");
        if (!joined.Level.TryAddEntity(joined))
        {
            joined.Level.TryRemovePlayer(joined);
            throw new InvalidOperationException($"Player '{joined.Username}' could not be registered as a level entity.");
        }

        server.BroadcastMessage(new ChatMessage
        {
            Text = string.Format(server.Configuration.Messages.Join, e.Player.Username),
            Color = HexColor.Yellow
        });

        var addAction = new AddPlayerInfoAction
        {
            Name = joined.Username,
        };

        if (server.Configuration.OnlineMode)
            addAction.Properties.AddRange(joined.SkinProperties);

        var list = new List<InfoAction>()
        {
            addAction,
            new UpdatePingInfoAction(joined.Ping),
            new UpdateListedInfoAction(joined.ClientInformation.AllowsListing),
        };

        packetBroadcaster.Broadcast(new PlayerInfoUpdatePacket(new Dictionary<Guid, List<InfoAction>>()
        {
            { joined.Uuid, list }
        }));

        // Show the new player to everyone already nearby. A joining client sees the players around it through its own
        // visibility updates, which run whenever it moves.
        joined.SpawnEntity();

        return default;
    }
}
