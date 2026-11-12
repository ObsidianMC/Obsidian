using Obsidian.API.Blocks;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Features;
using System.Collections.Frozen;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>HoneycombItem.useOn</c>: honeycomb turns an unwaxed copper block into its waxed counterpart, with
/// the same properties. Axes take the wax off again through 26.3's block transformers.
/// </summary>
internal static class CopperWaxing
{
    // Vanilla's level event for the wax-on particles.
    private const int WaxOnEvent = 3003;

    // Each waxable block's waxed state, by registry id. Every waxed block in 26.3 is named "waxed_" and the name of the
    // block it waxes, and those pairs are exactly HoneycombItem.WAXABLES: the four weathering states of copper blocks,
    // cut, chiseled, slabs, stairs, doors, trapdoors, bars, grates, bulbs, chests, golem statues, lightning rods,
    // lanterns and chains.
    private static readonly FrozenDictionary<int, IBlock> waxed = BuildTable();

    private static FrozenDictionary<int, IBlock> BuildTable()
    {
        var table = new Dictionary<int, IBlock>();
        foreach (var identifier in BlocksRegistry.ResourceIds)
        {
            var waxedIdentifier = "minecraft:waxed_" + identifier["minecraft:".Length..];
            if (BlocksRegistry.GetBlockName(waxedIdentifier) is null)
                continue;

            var block = BlocksRegistry.Get(identifier);
            table[block.RegistryId] = BlocksRegistry.Get(waxedIdentifier);
        }

        return table.ToFrozenDictionary();
    }

    /// <summary>
    /// The waxed counterpart of <paramref name="block"/> with its properties, or null when it can't be waxed.
    /// </summary>
    internal static IBlock? Waxed(IBlock block)
    {
        if (!waxed.TryGetValue(block.RegistryId, out var waxedBlock))
            return null;

        return ItemUseOnBehaviors.CopyProperties(waxedBlock, block);
    }

    /// <summary>
    /// Waxes the clicked block, using up one honeycomb outside creative mode. The block and the hand change together,
    /// so a hand that changes during the use neither loses honeycomb without a wax nor waxes twice.
    /// </summary>
    internal static async ValueTask<UseResult> UseHoneycombAsync(BlockUse use)
    {
        if (Waxed(use.Block) is not IBlock waxedState)
            return UseResult.Pass;

        // Vanilla fires item_used_on_block from useOn before the write, so this one sees the unwaxed block and the world
        // around it; the packet handler fires it again once the use succeeded. It's matched now and granted only once
        // the wax commits, so a use the commit refuses grants nothing.
        var player = use.Player;
        var beforeWax = player.MatchProgress(
            "item_used_on_block",
            item: use.UsedItem,
            block: use.Block,
            position: use.Position);

        var level = use.Level;
        var committed = use.TryCommit(
            () => level.BlockUpdates.SetBlock(use.Position, waxedState, BlockUpdateFlags.AllImmediate),
            consumeItem: true);
        if (!committed)
            return UseResult.Fail;

        await player.GrantProgressAsync(beforeWax);

        level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, player, affectedBlock: waxedState);
        PlayEffects(level, use.Position, use.Position, player.EntityId);

        // A double chest's other half takes the wax by its shape update; vanilla marks it as changed too, and plays the
        // sound at the clicked half a second time.
        if (ConnectedChest(use.Block, use.Position) is Vector other)
        {
            var otherState = level.BlockUpdates.GetBlock(other);
            level.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)other + 0.5, player, affectedBlock: otherState);
            PlayEffects(level, other, use.Position, player.EntityId);
        }

        return UseResult.Success;
    }

    // The wax-on particles at the block and the sound, for everyone but the user, whose client plays both itself.
    private static void PlayEffects(AbstractLevel level, Vector particles, Vector sound, int user)
    {
        var particleEvent = new LevelEventPacket(WaxOnEvent, particles, 0);
        level.PacketBroadcaster.BroadcastToLevelInRange(level, particles, particleEvent, user);
        BlockLighting.Play(level, sound, "minecraft:item.honeycomb.wax_on", except: user);
    }

    // Vanilla's ChestBlock.getConnectedBlockPos for a chest half that's part of a double chest.
    private static Vector? ConnectedChest(IBlock chest, Vector position)
    {
        var half = chest.GetProperty("type");
        if (half is not ("left" or "right"))
            return null;

        var facing = FeatureHelpers.ParseFace(chest.GetProperty("facing"));
        var direction = half == "left" ? facing.ClockWise() : facing.CounterClockWise();
        return position + direction.ToVector();
    }
}
