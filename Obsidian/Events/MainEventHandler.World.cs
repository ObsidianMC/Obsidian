using Obsidian.API.Inventory;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.API.Loot;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    [EventPriority(Priority = Priority.Internal)]
    public async ValueTask OnBlockBreak(BlockBreakEventArgs args)
    {
        var player = args.Player;
        var sequence = args.Sequence;
        var block = args.Block;
        var world = player.Level;
        var location = args.Location;

        player.SendPacket(new BlockChangedAckPacket
        {
            SequenceID = sequence
        });

        if (args.IsCancelled)
        {
            player.SendPacket(new BlockUpdatePacket(location, block.GetHashCode()));
            return;
        }

        var tool = player.GetHeldItem();
        var parts = await TwoPartBlockBreak.PrepareAsync(world, location, block);

        // What the block entity gives the loot (a shulker box's items, say), taken before the block goes.
        var blockEntity = await BlockEntityComponents.CaptureAsync(world, parts.DropPosition, player);
        var drops = await BlockLoot.GetDropsAsync(
            world,
            parts.DropPosition,
            parts.DropState,
            tool,
            breaker: player,
            blockEntityComponents: blockEntity.Components,
            dynamicDrops: blockEntity.DynamicDrops);

        await parts.RemovePartnerAsync(world);
        await world.SetBlockAsync(location, BlocksRegistry.Air, true);
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
