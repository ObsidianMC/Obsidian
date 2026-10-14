using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Events;
using Obsidian.API.BlockEntities;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.API.Loot;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    [EventPriority(Priority = Priority.Internal)]
    public ValueTask OnBlockBreak(BlockBreakEventArgs args)
    {
        if (args.Level is AbstractLevel level && BlocksRegistry.TryGetPluginBlock(args.Block.GetHashCode(), out _))
        {
            // The entire transaction shares the tick with block entity callbacks and plugin unloading.
            level.EnqueueEntityAction(() => this.BreakBlockAsync(args, onLevelTick: true));
            return ValueTask.CompletedTask;
        }

        return this.BreakBlockAsync(args);
    }

    private async ValueTask BreakBlockAsync(BlockBreakEventArgs args, bool onLevelTick = false)
    {
        var player = args.Player;
        var sequence = args.Sequence;
        var block = args.Block;
        var world = args.Level;
        var location = args.Location;

        player.SendPacket(new BlockChangedAckPacket
        {
            SequenceID = sequence
        });

        if (player.Level != world)
            return;

        var current = await world.GetBlockAsync(location) ?? BlocksRegistry.Air;
        if (args.IsCancelled || current.GetHashCode() != block.GetHashCode())
        {
            await RestoreBreakAsync(player, world, location, current);
            return;
        }

        if (await Plugins.BlockBehaviors.CancelPlayerBreakAsync(player, location, block))
        {
            current = await world.GetBlockAsync(location) ?? BlocksRegistry.Air;
            await RestoreBreakAsync(player, world, location, current);
            return;
        }

        int toolSlot;
        int wear;
        ItemStack? tool;
        using (await player.Inventory.LockAsync())
        {
            toolSlot = player.CurrentHeldItemSlot;
            tool = player.Inventory.GetItem(toolSlot);
            wear = CombatItems.ToolDamage(tool, block);
        }

        var parts = await TwoPartBlockBreak.PrepareAsync(world, location, block);

        // What the block entity gives the loot (a shulker box's items, say), taken before the block goes.
        await using var blockEntity = await BlockEntityComponents.CaptureAsync(
            world, parts.DropPosition, player, onLevelTick);
        if (!blockEntity.CanBreak)
            return;

        var drops = await BlockLoot.GetDropsAsync(
            world,
            parts.DropPosition,
            parts.DropState,
            tool,
            breaker: player,
            blockEntityComponents: blockEntity.Components,
            dynamicDrops: blockEntity.DynamicDrops);

        if (world is AbstractLevel concreteWorld)
        {
            var changed = await concreteWorld.TrySetBlockAsync(location, BlocksRegistry.Air, BlockUpdateFlags.All,
                state => state.GetHashCode() == block.GetHashCode()
                    && (blockEntity.Source is null || parts.DropPosition != location
                        || ReferenceEquals(concreteWorld.GetResidentBlockEntity(location), blockEntity.Source)));
            if (!changed)
                return;
        }
        else
        {
            await world.SetBlockAsync(location, BlocksRegistry.Air, true);
        }

        await parts.RemovePartnerAsync(world);
        BlockSoundPlayback.Play(world, block, location, breaking: true, player.EntityId);

        if (world is AbstractLevel gameEvents)
        {
            if (block.Is(BlockIds.Jukebox))
                gameEvents.StopJukebox(location);

            gameEvents.EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)location + 0.5, player,
                affectedBlock: block);
        }

        if (player is Player concrete)
            concrete.AddExhaustion(0.005f);

        player.SendPacket(new BlockUpdatePacket(location, BlocksRegistry.Air.GetHashCode()));

        world.PacketBroadcaster.QueuePacketToLevel(world, 0, new BlockDestructionPacket
        {
            EntityId = player.EntityId,
            Position = location,
            DestroyStage = -1
        }, player.EntityId);

        if (player.GameMode is GameMode.Creative or GameMode.Spectator)
        {
            // Vanilla's ShulkerBoxBlock.playerWillDestroy: a creative player's filled box still drops, holding its
            // items, which would be lost otherwise.
            if (player.GameMode == GameMode.Creative
                && blockEntity.HoldsItems
                && BlocksRegistry.TryGetItem(parts.DropState, out var box))
            {
                SpawnBlockDrop(player, parts.DropPosition, new ItemStack(box, 1, [.. blockEntity.Components]));
            }

            return;
        }

        if (player is Player statisticsMiner)
            statisticsMiner.Statistics.Increment(block.UnlocalizedName, type: "minecraft:mined");

        // Like vanilla's Item.mineBlock, the tool wears once the drops were decided with it as it was.
        if (player is Player miner && tool is not null && wear > 0)
            await miner.DamageInventoryItemAsync(toolSlot, wear, expected: tool);

        if (block.UnlocalizedName.StartsWith("minecraft:infested_", StringComparison.Ordinal))
        {
            var silkTouch = tool is not null && EnchantmentHelper.GetEnchantments(tool).Any(enchantment =>
                enchantment.Id == EnchantmentsRegistry.SilkTouch.Id && enchantment.Level > 0);
            if (!silkTouch)
            {
                if (world is AbstractLevel level)
                {
                    level.EnqueueEntityAction(() =>
                    {
                        Silverfish.SpawnFromBlock(world, location);
                        return default;
                    });
                }
                else
                {
                    Silverfish.SpawnFromBlock(world, location);
                }
            }
        }

        foreach (var droppedItem in drops)
            SpawnBlockDrop(player, parts.DropPosition, droppedItem);
    }

    private static async ValueTask RestoreBreakAsync(IPlayer player, ILevel world, Vector location, IBlock block)
    {
        player.SendPacket(new BlockUpdatePacket(location, block.GetHashCode()));

        // Prediction discarded the client's block entity. Restore its retained data after recreating the block.
        if (await world.GetBlockEntityAsync(location) is not BlockEntity entity || entity.IsRemoved)
            return;

        var data = entity.SavesOnAnyThread ? entity.GetUpdateData() : entity.ClientData;
        var type = BlockEntityTypesRegistry.NetworkId(entity.Id);
        if (data is not null && type >= 0)
        {
            player.SendPacket(new BlockEntityDataPacket
            {
                Position = location,
                Type = type,
                NBTData = DataBlockEntity.Copy(data)
            });
        }
    }

    private static void SpawnBlockDrop(IPlayer player, Vector position, ItemStack droppedItem)
    {
        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Item = droppedItem,
            Level = player.Level,
            Position = (VectorD)position + 0.5
        };

        player.Level.TryAddEntity(item);

        var power = GetRandDropVelocity();
        var direction = Globals.Random.NextSingle() * 6.2f;

        item.SpawnEntity(new Velocity(-Math.Sin(direction) * power, 0.2f, Math.Cos(direction) * power));
    }

    private static float GetRandDropVelocity()
    {
        var f = Globals.Random.NextSingle();

        return f * 0.1f;
    }
}
