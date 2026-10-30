using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>ComposterBlock</c>: a composter's <c>level</c> (0 to 8) rises as compostable items are put in, each
/// by the layers it adds; at 7 it fills for a tick and becomes ready (8), when bone meal can be taken from it. Items
/// go in from above (<c>useItemOn</c>, or a hopper above) and bone meal comes out from below (<c>useWithoutItem</c>,
/// or a hopper below).
/// </summary>
/// <remarks>
/// What composts, and how much, comes from items' <c>minecraft:compostable</c> (26.3). Items thrown into a composter
/// (item entities) aren't handled yet.
/// </remarks>
internal static class Composters
{
    // Vanilla's ComposterBlock.READY and the level at which a composter is full and waits to become ready.
    internal const int Ready = 8;
    internal const int Full = 7;

    /// <summary>Vanilla's <c>ComposterBlock.tick</c> delay for a full composter to become ready.</summary>
    internal const int ReadyDelay = 20;

    /// <summary>
    /// The layers <paramref name="item"/> adds to a composter (its <c>minecraft:compostable</c>), or null if it isn't
    /// compostable.
    /// </summary>
    internal static ComponentIntValue? LayersOf(ItemStack item) => ItemFuels.Compostable(item);

    /// <summary>The composter's level, or -1 if the block isn't one.</summary>
    internal static int LevelOf(IBlock block) =>
        block.Is(BlockIds.Composter) && int.TryParse(block.GetProperty("level"), out var level) ? level : -1;

    /// <summary>
    /// Vanilla's <c>ComposterBlock.addLayer</c> (26.3): the item is consumed whether or not it raises the level. Its
    /// layers are worked out for the composter's block (vanilla's always add one to an empty composter and roll their
    /// chance otherwise) and raise the level up to full. The fill event reports whether it rose.
    /// </summary>
    internal static IBlock Filled(IBlock block, ComponentIntValue layers, Random random, out bool rose)
    {
        var level = LevelOf(block);
        var added = ComponentNumbers.Resolve(layers, block, random);
        rose = added > 0;

        if (!rose)
            return block;

        var next = Math.Clamp(level + added, 0, Full);
        return block.WithProperty("level", next.ToString());
    }

    /// <summary>Plays a composter sound to the players in range, from its block.</summary>
    internal static void PlaySound(AbstractLevel level, Vector position, string sound)
    {
        level.PacketBroadcaster.BroadcastToLevelInRange(level, position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });
    }
}
