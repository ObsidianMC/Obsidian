using Microsoft.Extensions.Logging;
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

        // Item frames, paintings and leash knots react to hits themselves.
        if (entity is IPlayerInteractable hit && attacker is PlayerBase hitter)
        {
            ValueTask Hit() => entity.Level == hitter.Level && hitter.CanAttack(entity) ? hit.AttackedAsync(hitter) : default;

            if (entity.Level is Obsidian.WorldData.AbstractLevel hitLevel)
                hitLevel.EnqueueEntityAction(Hit);
            else
                await Hit();
            return;
        }

        if (entity is IPlayer || entity is Mob { HasAi: true } || entity is EndCrystal or EnderDragonPart || entity is MobProjectile { Type: EntityType.Fireball })
        {
            async ValueTask Damage()
            {
                if (entity.Level == attacker.Level && attacker.CanAttack(entity, e.Weapon) &&
                    (entity is not Mob mob || mob.CanSee(attacker)))
                {
                    await attacker.ResolveAttackAsync(e);
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
        if (e.IsCancelled)
            return;

        // An armor stand's use is taken against the hand as the click found it (see ArmorStandUse).
        var hand = e.Hand ?? InteractionHand.MainHand;
        var standUse = e.Entity is ArmorStand && e.Player is PlayerBase user
            ? await ArmorStandUse.CaptureAsync(user, hand, e.TargetPosition)
            : null;

        // The entity's interaction and the item's use run on the entity's level tick, like the entity's own tick.
        ValueTask Interact() => InteractWithEntityAsync(e.Player, e.Entity, hand, standUse);
        if (e.Entity.Level is Obsidian.WorldData.AbstractLevel level)
            level.EnqueueEntityAction(Interact);
        else
            await Interact();
    }

    /// <summary>
    /// A player's use of an entity (right-clicking it) with the item in <paramref name="hand"/>.
    /// </summary>
    internal static async ValueTask InteractWithEntityAsync(IPlayer player, IEntity entity, InteractionHand hand,
        ArmorStandUse? standUse = null)
    {
        // Like vanilla's Player.interactOn, the entity's own interaction goes first, and the held item is used on a
        // living entity only when the entity didn't use the interaction. A plugin's projectile or object does what
        // its type's behaviour does, like a mob.
        var used = entity switch
        {
            PluginEntity pluginEntity => await pluginEntity.InteractAsync(player, hand),
            ArmorStand stand when standUse is not null => await stand.UseAsync(standUse),
            IPlayerInteractable interactable when player is PlayerBase user => await interactable.InteractAsync(user, hand),
            // Like vanilla's Mob.interact, leads come before the mob's own interaction.
            Mob { HasAi: true } mob => player is PlayerBase leader && await mob.UseLeashAsync(leader, hand)
                || await mob.InteractAsync(player, hand),
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

        var player = e.Player;

        if (player.Alive && e.Container is MerchantContainer merchant)
            await merchant.CloseAsync(player, notify: false);

        if (player.Alive && e.Container is CraftingTable { Type: InventoryType.Crafting })
            await ReturnCraftingItemsAsync(player, e.Container, 3);
        if (player.Alive && e.Container is EnchantmentTable enchantingTable)
            await ReturnEnchantingItemsAsync(player, enchantingTable);
        if (player.Alive && HoldsItemsWhileOpen(e.Container))
            await ReturnWorkstationItemsAsync(player, e.Container);

        // Only the window that is closing: a plugin's handler may have opened another one in its place.
        if (ReferenceEquals(player.OpenedContainer, e.Container) && player.CurrentContainerId == e.WindowId)
            player.OpenedContainer = null;

        player.RunMenusClosed();

    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task OnPlayerInteract(PlayerInteractEventArgs e)
    {
        if (e.IsCancelled)
            return;

        var item = e.Item;

        var block = e.Block;
        var server = e.Server as Server;
        var player = e.Player;

        if (block == null && player != null && item is { Count: > 0 } &&
            player.Vehicle is IItemSteerable steered && item.Is(steered.SteeringItem))
        {
            async ValueTask Boost()
            {
                if (!steered.Boost(player) || player.GameMode == GameMode.Creative)
                    return;
                var component = Obsidian.API.Inventory.DataComponents.ComponentBuilder.Damage with
                {
                    Value = item.Damage + steered.BoostItemDamage
                };
                item[DataComponentType.Damage] = component;
                var slot = e.Hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
                if (component.Value >= steered.SteeringItem.MaxDamage)
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

        // A block's menu, which the block's use asked for (see UseBehaviors).
        if (e.OpensBlockMenu && block is not null && e.BlockLocation is Vector blockPosition)
            await BlockMenus.OpenAsync(player, block, blockPosition);
    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task OnPlayerLeave(PlayerLeaveEventArgs e)
    {
        var player = e.Player;
        var server = e.Server;

        if (player.OpenedContainer is MerchantContainer merchant)
            await merchant.CloseAsync(player, notify: false);

        if (player.OpenedContainer is CraftingTable { Type: InventoryType.Crafting } table)
            await ReturnCraftingItemsAsync(player, table, 3);
        if (player.OpenedContainer is EnchantmentTable enchantingTable)
            await ReturnEnchantingItemsAsync(player, enchantingTable);
        if (HoldsItemsWhileOpen(player.OpenedContainer))
            await ReturnWorkstationItemsAsync(player, player.OpenedContainer!);
        await ReturnCraftingItemsAsync(player, player.Inventory, 2);

        await player.CancelPortalTravelAsync();

        API.Data.AttachmentStorage.For(player).Retire();
        await player.SaveAsync();

        await RemoveRiddenVehicleAsync(player);
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

    // Like vanilla's PlayerList.remove, the vehicle or mount a leaving player rides alone goes with them: it was saved
    // with them (see PlayerBase.WriteNbt), so it's removed rather than left empty in the world.
    private static async ValueTask RemoveRiddenVehicleAsync(IPlayer player)
    {
        if (player is not Entities.PlayerBase concrete || player.Level is not Obsidian.WorldData.AbstractLevel level)
            return;
        if (concrete.RootVehicle is not { Passengers: [var soleRider] } || !ReferenceEquals(soleRider, concrete)
            || ReferenceEquals(concrete.RootVehicle, concrete))
        {
            return;
        }

        // The vehicle's tick reads its passengers, so the removal runs between level ticks. The leave waits for it a
        // while; a level that isn't ticking leaves it queued, and it still runs with the guards checked again.
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        level.EnqueueEntityAction(async () =>
        {
            try
            {
                if (concrete.RootVehicle is not { Passengers: [var rider] } root
                    || !ReferenceEquals(rider, concrete)
                    || ReferenceEquals(root, concrete))
                {
                    removed.TrySetResult();
                    return;
                }

                concrete.StopRiding();
                await level.DestroyEntityAsync(root);
                removed.TrySetResult();
            }
            catch (Exception ex)
            {
                removed.TrySetException(ex);
            }
        });

        try
        {
            await removed.Task.WaitAsync(RiddenVehicleRemovalTimeout);
        }
        catch (TimeoutException)
        {
            Log.RiddenVehicleRemovalPending(player.Logger, player.Username);
        }
    }

    private static readonly TimeSpan RiddenVehicleRemovalTimeout = TimeSpan.FromSeconds(5);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "{Username}'s vehicle was not removed before they left; it will be removed on the next level tick")]
        public static partial void RiddenVehicleRemovalPending(ILogger logger, string username);
    }

    [EventPriority(Priority = Priority.Internal)]
    public ValueTask OnPlayerJoin(PlayerJoinEventArgs e)
    {
        var joined = e.Player;
        var server = e.Server;

        var packetBroadcaster = joined.Level.PacketBroadcaster;

        if (!joined.Level.TryAddPlayer(joined))
            throw new InvalidOperationException($"Player '{joined.Username}' is already registered in level '{joined.Level.Name}'.");
        // The saved position may lie in a region that isn't loaded yet (a join doesn't load it), and an entity can only be
        // added to a loaded region, so load the region first, as the chunk loading does.
        if (joined.Level is AbstractLevel level)
        {
            var (chunkX, chunkZ) = Region.ChunkOf(joined.Position);
            level.LoadRegionByChunk(chunkX, chunkZ);
        }

        if (!joined.Level.TryAddEntity(joined))
        {
            joined.Level.TryRemovePlayer(joined);
            throw new InvalidOperationException($"Player '{joined.Username}' could not be registered as a level entity.");
        }

        if (joined is Entities.PlayerBase concreteJoined)
            concreteJoined.RestoreSavedVehicle();

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
