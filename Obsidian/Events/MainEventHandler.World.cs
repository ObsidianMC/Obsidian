using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.WorldData;

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

        // The drops are decided from the block, its state and the held tool as they are before the block is removed.
        // Creative players get no drops, like vanilla, where breaking doesn't call playerDestroy.
        var tool = player.GetHeldItem() ?? ItemStack.Air;
        var drops = player.GameMode == GameMode.Creative || world is not AbstractLevel dropLevel
            ? []
            : await BlockDrops.ForPlayerBreakAsync(dropLevel, block, (VectorD)location, tool, player);

        // Creative breaks run vanilla's preventDropFromBottomPart: a double plant's lower half goes without a drop.
        if (player.GameMode == GameMode.Creative && world is Obsidian.WorldData.AbstractLevel creativeLevel)
            await Obsidian.WorldData.DoublePlants.PreventDropFromBottomPartAsync(creativeLevel, block, location);

        await world.SetBlockAsync(location, BlocksRegistry.Air, true);
        if (world is Obsidian.WorldData.AbstractLevel gameEvents)
        {
            if (block.Material == Material.Jukebox) gameEvents.StopJukebox(location);
            gameEvents.EmitGameEvent(Obsidian.WorldData.MobGameEvent.BlockDestroy, (VectorD)location + new VectorD(0.5, 0.5, 0.5), player, affectedBlock: block);
        }
        if (player is Player concrete) concrete.AddExhaustion(0.005f);

        player.SendPacket(new BlockUpdatePacket(location, BlocksRegistry.Air.GetHashCode()));

        world.PacketBroadcaster.QueuePacketToLevel(world, 0, new BlockDestructionPacket
        {
            EntityId = player.EntityId,
            Position = location,
            DestroyStage = -1
        }, player.EntityId);

        // Infested blocks release a silverfish unless broken with Silk Touch; that's a behaviour of the block, not of its loot.
        if (block.UnlocalizedName.StartsWith("minecraft:infested_", StringComparison.Ordinal) && player.GameMode != GameMode.Creative)
        {
            var silkTouch = EnchantmentHelper.GetLevel(tool, EnchantmentsRegistry.SilkTouch) > 0;
            if (!silkTouch)
            {
                if (world is Obsidian.WorldData.AbstractLevel level)
                    level.EnqueueEntityAction(() => { Silverfish.SpawnFromBlock(world, location); return default; });
                else
                    Silverfish.SpawnFromBlock(world, location);
            }
        }

        foreach (var stack in drops)
            BlockDrops.SpawnDrop(world, () => Server.GetNextEntityId(), location, stack);

        // The held tool wears after its loot is decided, as vanilla's Item.mineBlock does. Creative tools don't wear, and
        // a block with zero hardness (instant to break) doesn't cost durability.
        if (player is Player wearer && wearer.GameMode != GameMode.Creative && !tool.IsAir &&
            BlockPhysics.MiningHardness(block.StateId()) != 0 && BlockDrops.EffectiveTool(tool) is { DamagePerBlock: > 0 } wear)
        {
            var broke = CombatItems.HurtItem(tool, wear.DamagePerBlock);
            var slot = wearer.CurrentHeldItemSlot;
            wearer.Inventory.SetItem(slot, broke ? ItemStack.Air : tool);
            wearer.SendPacket(new ContainerSetSlotPacket
            {
                Slot = slot,
                StateId = wearer.Inventory.StateId++,
                SlotData = wearer.Inventory.GetItem(slot)
            });
        }
    }

    private static float GetRandDropVelocity()
    {
        var f = Globals.Random.NextSingle();

        return f * 0.1f;
    }
}
