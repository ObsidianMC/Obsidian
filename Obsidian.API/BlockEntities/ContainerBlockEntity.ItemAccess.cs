using Obsidian.API.Inventory;

namespace Obsidian.API.BlockEntities;

public partial class ContainerBlockEntity
{
    // Every slot in order, by inventory size, shared by the containers that reach all their slots.
    private static readonly int[][] allSlots = new int[MaxSlots + 1][];

    /// <summary>
    /// The access other blocks have to the container's items from <paramref name="side"/>: the slots
    /// <see cref="GetSlots"/> gives, under the rules of <see cref="CanInsert"/> and <see cref="CanExtract"/>.
    /// </summary>
    /// <remarks>
    /// Each call through it is atomic with players' clicks in the container's windows, and changes nothing once the
    /// block entity is removed (its inventory is retired). A real change calls <see cref="BlockEntity.SetChanged"/>,
    /// and the players with the container open see it at the end of the level's tick. Reaching an unopened
    /// container rolls its loot table first, as vanilla's hoppers do.
    /// </remarks>
    public virtual IItemAccess? GetItemAccess(BlockFace? side) => new ContainerItemAccess(this, side);

    /// <summary>
    /// The slots of <see cref="Inventory"/> reached from <paramref name="side"/>, in the order they're filled and
    /// emptied, like vanilla's <c>WorldlyContainer.getSlotsForFace</c>; all of them from no particular side. All of
    /// them, in order, by default.
    /// </summary>
    protected internal virtual IReadOnlyList<int> GetSlots(BlockFace? side) => AllSlots(this.Inventory.Size);

    /// <summary>
    /// Whether <paramref name="stack"/> may be put into <paramref name="slot"/> from <paramref name="side"/> (null
    /// for no particular side), like vanilla's <c>canPlaceItem</c> and <c>canPlaceItemThroughFace</c>. Called with
    /// the inventory's lock held: read <see cref="Inventory"/>, don't wait on anything. True by default.
    /// </summary>
    protected internal virtual bool CanInsert(int slot, ItemStack stack, BlockFace? side) => true;

    /// <summary>
    /// Whether <paramref name="stack"/>, in <paramref name="slot"/>, may be taken out from <paramref name="side"/>
    /// (null for no particular side), like vanilla's <c>canTakeItemThroughFace</c>. Called with the inventory's
    /// lock held. True by default.
    /// </summary>
    protected internal virtual bool CanExtract(int slot, ItemStack stack, BlockFace? side) => true;

    /// <summary>
    /// The most of <paramref name="stack"/>'s item <paramref name="slot"/> holds when filled through item access:
    /// the item's stack size by default.
    /// </summary>
    internal virtual int SlotLimit(int slot, ItemStack stack) => stack.MaxStackSize;

    /// <summary>
    /// Called with the inventory's lock held just before items are put into the container through item access, for
    /// a hopper's transfer cooldown.
    /// </summary>
    internal virtual void OnInserting()
    {
    }

    /// <summary>
    /// The slots from 0 to <paramref name="size"/> - 1, in order.
    /// </summary>
    internal static int[] AllSlots(int size)
    {
        if ((uint)size > MaxSlots)
            return [.. Enumerable.Range(0, size)];

        return allSlots[size] ??= [.. Enumerable.Range(0, size)];
    }
}

/// <summary>
/// What the server does for item access to a container block entity placed in a level: its block entity host
/// implements it.
/// </summary>
internal interface IContainerItemsHost
{
    /// <summary>
    /// Readies the container's items before item access reads or changes them: rolls the loot table it's waiting
    /// for. Called without the inventory's lock.
    /// </summary>
    /// <returns>
    /// False when it can't be done now, where rolling the loot could wait on the level's tick lock the caller holds;
    /// it's rolled on the level's next tick instead, and the call takes and gives nothing.
    /// </returns>
    public bool PrepareItems(ContainerBlockEntity container);

    /// <summary>
    /// Whether the calling thread holds the level's tick lock, where waiting for a player's click could deadlock.
    /// </summary>
    public bool HoldsTickLock { get; }

    /// <summary>
    /// Queues what follows a change made through item access: the windows showing the container are sent again, and
    /// the comparators reading it updated, at the end of the level's tick.
    /// </summary>
    public void OnItemsChanged(ContainerBlockEntity container);
}

/// <summary>
/// Item access to a container block entity from one side (see <see cref="ContainerBlockEntity.GetItemAccess"/>).
/// </summary>
/// <remarks>
/// Locks, in the order they're taken: the level's tick lock (when the caller holds it), an item entity's transfer lock
/// (when a hopper takes in an item entity), the inventory's click lock (<see cref="BaseContainer.Lock"/>), then the
/// loot lock, which <see cref="ContainerBlockEntity.Save"/> takes in the same order. A call takes one inventory's lock at a time, never waits on anything else holding it, and rolls
/// pending loot before taking it, so that it never holds a click lock while a chunk loads. Holding the tick lock, it
/// doesn't wait for a click lock at all: a container busy at that moment takes and gives nothing.
/// </remarks>
internal sealed class ContainerItemAccess(ContainerBlockEntity owner, BlockFace? side) : IItemAccess
{
    private readonly IReadOnlyList<int> slots = owner.GetSlots(side);

    public int SlotCount => this.slots.Count;

    public ItemStack? GetItem(int slot)
    {
        var index = this.Index(slot);
        using var gate = this.Enter();
        if (gate is null)
            return null;

        return owner.Inventory[index] is { IsAir: false, Count: > 0 } stack
            ? new ItemStack(stack, stack.Count)
            : null;
    }

    public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var index = this.Index(slot);
        if (stack.IsAir || stack.Count <= 0)
            return null;

        using (var gate = this.Enter())
        {
            if (gate is null || !owner.CanInsert(index, stack, side))
                return stack;

            var existing = owner.Inventory[index];
            var empty = existing is null || existing.IsAir || existing.Count <= 0;
            var limit = owner.SlotLimit(index, stack);

            // Vanilla's HopperBlockEntity.tryMoveInItem: an empty slot takes the stack, a stack of the same item with
            // the same components tops up to the moving stack's size.
            int moved;
            if (empty)
                moved = Math.Min(stack.Count, limit);
            else if (existing!.Equals(stack) && existing.Count <= existing.MaxStackSize)
                moved = Math.Clamp(limit - existing.Count, 0, stack.Count);
            else
                moved = 0;

            if (moved <= 0)
                return stack;

            if (!simulate)
            {
                owner.OnInserting();
                owner.Inventory.SetItem(index, new ItemStack(stack, empty ? moved : existing!.Count + moved));
                gate.Changed = true;
            }

            return moved == stack.Count ? null : new ItemStack(stack, stack.Count - moved);
        }
    }

    public ItemStack? Extract(int slot, int count, bool simulate = false)
    {
        var index = this.Index(slot);
        if (count <= 0)
            return null;

        using var gate = this.Enter();
        if (gate is null)
            return null;

        var existing = owner.Inventory[index];
        if (existing is null || existing.IsAir || existing.Count <= 0 || !owner.CanExtract(index, existing, side))
            return null;

        var taken = Math.Min(count, existing.Count);
        var result = new ItemStack(existing, taken);

        if (!simulate)
        {
            var rest = existing.Count - taken;
            owner.Inventory.SetItem(index, rest > 0 ? new ItemStack(existing, rest) : null);
            gate.Changed = true;
        }

        return result;
    }

    private int Index(int slot) => (uint)slot < (uint)this.slots.Count
        ? this.slots[slot]
        : throw new ArgumentOutOfRangeException(nameof(slot), slot, null);

    // Takes the inventory's lock once its loot is rolled, or returns null when the container takes and gives nothing:
    // it was removed, its loot can't be rolled now, or (holding the tick lock) a click has it.
    private Gate? Enter()
    {
        var host = owner.Host as IContainerItemsHost;
        if (host is not null && !host.PrepareItems(owner))
            return null;

        var inventory = owner.Inventory;
        IDisposable held;
        if (host is { HoldsTickLock: true })
        {
            var gate = inventory.TransactionLock;
            if (!gate.Wait(0))
                return null;

            held = new LockRelease(gate);
        }
        else
        {
            held = inventory.Lock();
        }

        // Checked again under the lock: the block entity may have gone, or been given a loot table, meanwhile.
        if (owner.IsRemoved || inventory.IsRetired || owner.LootTable is not null)
        {
            held.Dispose();
            return null;
        }

        return new Gate(owner, host, held);
    }

    // The inventory's lock for one call, which on release tells the server about a change the call made.
    private sealed class Gate(ContainerBlockEntity owner, IContainerItemsHost? host, IDisposable held) : IDisposable
    {
        public bool Changed { get; set; }

        public void Dispose()
        {
            held.Dispose();

            if (!this.Changed)
                return;

            owner.SetChanged();
            host?.OnItemsChanged(owner);
        }
    }
}
