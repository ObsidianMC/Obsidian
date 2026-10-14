using Obsidian.API.Blocks;

namespace Obsidian.API.Inventory;

/// <summary>
/// A way into the items of something, from one side: a chest's slots, a furnace's slots as a hopper on a given face
/// reaches them, or a plugin machine's buffers. Like NeoForge's item handler capability: what a hopper, a dropper, a
/// pipe or a storage network moves items through. Get one with <see cref="IBlockLevel.GetItemAccess"/> or
/// <see cref="ILevel.GetItemAccess"/>.
/// </summary>
/// <remarks>
/// <para>
/// Slots are numbered from 0 to <see cref="SlotCount"/> - 1 in the order the side reaches them, which is the order
/// <see cref="Insert(ItemStack, bool)"/> fills them in. From a side that reaches only some of a container's slots
/// (a furnace's top reaches its ingredient slot), slot 0 is the first slot that side reaches, not the container's
/// first slot.
/// </para>
/// <para>
/// With <c>simulate</c> set, a call reports what it would do and changes nothing, so that a transfer can be planned
/// on both ends before either end changes: simulate the extraction and the insertion, then do both. Each call is
/// atomic, but two calls aren't one transaction: another player's click can come between them, so check what the
/// real call returns rather than assuming it matches the simulation.
/// </para>
/// <para>
/// Stacks passed in are never changed, and stacks returned are the caller's own copies.
/// </para>
/// </remarks>
public interface IItemAccess
{
    /// <summary>
    /// How many slots the side reaches. A container that was removed keeps its count, but takes and gives nothing.
    /// </summary>
    public int SlotCount { get; }

    /// <summary>
    /// A copy of the stack in <paramref name="slot"/>, or null when it's empty.
    /// </summary>
    public ItemStack? GetItem(int slot);

    /// <summary>
    /// Puts as much of <paramref name="stack"/> as fits into <paramref name="slot"/>: into an empty slot, or onto a
    /// stack of the same item with the same components, up to its stack size, if the slot takes the item from this
    /// side.
    /// </summary>
    /// <returns>What didn't go in, or null when all of it did.</returns>
    public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false);

    /// <summary>
    /// Puts as much of <paramref name="stack"/> as fits into the slots, in their order, like a hopper fills a
    /// container: each slot in turn tops up a matching stack or takes the rest when empty.
    /// </summary>
    /// <returns>What didn't go in, or null when all of it did.</returns>
    public ItemStack? Insert(ItemStack stack, bool simulate = false)
    {
        ItemStack? left = stack;
        for (var slot = 0; slot < this.SlotCount && left is not null; slot++)
            left = this.Insert(slot, left, simulate);

        return left;
    }

    /// <summary>
    /// Takes up to <paramref name="count"/> items out of <paramref name="slot"/>, if the slot gives them up to this
    /// side.
    /// </summary>
    /// <returns>What came out, or null when nothing did.</returns>
    public ItemStack? Extract(int slot, int count, bool simulate = false);
}

/// <summary>
/// Something with items other code can reach from a side (see <see cref="IItemAccess"/>): implement it on a block
/// entity to expose its items. <see cref="BlockEntities.ContainerBlockEntity"/> implements it already.
/// </summary>
public interface IItemAccessProvider
{
    /// <summary>
    /// The access to the items from <paramref name="side"/>, the face of this block another block reaches it through
    /// (a hopper above a block reaches it through its <see cref="BlockFace.Up"/> face); or from no particular side
    /// when null, like the block's own code. Null when nothing can be reached from that side.
    /// </summary>
    public IItemAccess? GetItemAccess(BlockFace? side);
}

/// <summary>
/// A plugin block asked for access to its items from a side (see <see cref="IBlockBehavior.GetItemAccess"/>).
/// </summary>
public sealed class ItemAccessContext : BlockContext
{
    /// <summary>
    /// The face of the block reached through, or null for no particular side (see
    /// <see cref="IItemAccessProvider.GetItemAccess"/>).
    /// </summary>
    public required BlockFace? Side { get; init; }
}
