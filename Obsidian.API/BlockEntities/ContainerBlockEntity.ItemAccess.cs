using Obsidian.API.Inventory;
using System.Threading;

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
    /// The most of <paramref name="stack"/>'s item <paramref name="slot"/> holds through item access or menu clicks:
    /// the item's stack size by default. The slot is the inventory's index, not a side's item access index.
    /// Called with the inventory's lock held, from any thread: read <see cref="Inventory"/>, don't wait on anything.
    /// Return zero to refuse insertion; the item's own stack size remains an upper bound.
    /// </summary>
    protected internal virtual int SlotLimit(int slot, ItemStack stack) => stack.StoredMaxStackSize;

    // Menu clicks reach the owning plugin through its host, including when another plugin's menu shows the slot.
    internal int MenuSlotLimit(int slot, ItemStack stack) =>
        this.IsRemoved ? 0
        : this.Host is IContainerItemsHost host ? host.SlotLimit(this, slot, stack)
        : this.SlotLimit(slot, stack);

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
    /// <summary>Reads a menu slot's limit inside a call into the container's owning plugin.</summary>
    public int SlotLimit(ContainerBlockEntity container, int slot, ItemStack stack);

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

    /// <summary>
    /// Queues the container's <see cref="ContainerBlockEntity.OnItemsChanged"/> to the end of the level's tick.
    /// </summary>
    public void QueueItemsChanged(ContainerBlockEntity container);
}

/// <summary>
/// Item access to a container block entity from one side (see <see cref="ContainerBlockEntity.GetItemAccess"/>).
/// </summary>
/// <remarks>
/// Locks, in the order they're taken: the level's tick lock (when the caller holds it), an item entity's transfer lock
/// (when a hopper takes in an item entity), the inventory's click lock (<see cref="BaseContainer.Lock"/>), then the
/// loot lock, which <see cref="ContainerBlockEntity.Save"/> takes in the same order. A call takes one inventory's lock at a time, never waits on anything else holding it, and rolls
/// pending loot before taking it, so that it never holds a click lock while a chunk loads. Holding the tick lock, it
/// doesn't wait for a click lock at all: a container busy at that moment takes and gives nothing. Inserting a whole
/// stack holds the lock across the slots it fills, so that no click comes between them.
/// </remarks>
internal sealed class ContainerItemAccess(ContainerBlockEntity owner, BlockFace? side) : IItemAccess
{
    // Set while a plugin's menu calls item access holding its click locks (see WithoutWaiting).
    [ThreadStatic]
    private static bool withoutWaiting;

    private readonly IReadOnlyList<int> slots = owner.GetSlots(side);

    /// <summary>
    /// Runs <paramref name="call"/> so that a container's item access in it never waits: a container busy with a
    /// click, or still waiting for its loot table, takes and gives nothing, like inside the tick lock. A plugin's menu
    /// calls item access this way while it holds click locks, which waiting could take out of order.
    /// </summary>
    internal static T WithoutWaiting<T>(Func<T> call)
    {
        var outer = withoutWaiting;
        withoutWaiting = true;

        try
        {
            return call();
        }
        finally
        {
            withoutWaiting = outer;
        }
    }

    public int SlotCount => this.slots.Count;

    public ItemStack? GetItem(int slot)
    {
        var index = this.Index(slot);
        if (!this.TryEnter(out var entry))
            return null;

        try
        {
            return owner.Inventory[index] is { IsAir: false, Count: > 0 } stack
                ? new ItemStack(stack, stack.Count)
                : null;
        }
        finally
        {
            this.Exit(entry, changed: false);
        }
    }

    /// <summary>
    /// Whether every slot holds what <paramref name="matches"/> accepts: its stack, or null when it's empty. Gives
    /// what <see cref="GetItem"/> for each slot in turn would, a busy or removed container reading as empty, but
    /// reads the stacks under one hold of the lock without copying them. <paramref name="matches"/> runs holding the
    /// lock: it must only read, and must not run plugin code, such as a component's copy (read
    /// <see cref="ItemStack.StoredMaxStackSize"/>, not <see cref="ItemStack.MaxStackSize"/>).
    /// </summary>
    internal bool AllSlots(Func<ItemStack?, bool> matches)
    {
        // With no slots, there's nothing to read, and so no loot to roll.
        if (this.slots.Count == 0)
            return true;

        // Every slot of a busy or removed container reads as empty.
        if (!this.TryEnter(out var entry))
            return matches(null);

        try
        {
            for (var slot = 0; slot < this.slots.Count; slot++)
            {
                var stack = owner.Inventory[this.slots[slot]] is { IsAir: false, Count: > 0 } held ? held : null;
                if (!matches(stack))
                    return false;
            }

            return true;
        }
        finally
        {
            this.Exit(entry, changed: false);
        }
    }

    public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var index = this.Index(slot);
        if (stack.IsAir || stack.Count <= 0)
            return null;

        if (!this.TryEnter(out var entry))
            return stack;

        var changed = false;
        try
        {
            return this.InsertHeld(index, stack, simulate, ref changed);
        }
        finally
        {
            this.Exit(entry, changed);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Fills the slots in their order under one hold of the inventory's lock, which gives what the slot-by-slot calls
    /// of <see cref="IItemAccess.Insert(ItemStack, bool)"/> give when nothing comes between them.
    /// </remarks>
    public ItemStack? Insert(ItemStack stack, bool simulate = false)
    {
        ArgumentNullException.ThrowIfNull(stack);

        // As the slot-by-slot calls answer: no slots leave the stack, and an empty stack goes into the first slot.
        if (this.slots.Count == 0)
            return stack;

        if (stack.IsAir || stack.Count <= 0)
            return null;

        if (!this.TryEnter(out var entry))
            return stack;

        var changed = false;
        try
        {
            ItemStack? left = stack;
            for (var slot = 0; slot < this.slots.Count && left is not null; slot++)
                left = this.InsertHeld(this.slots[slot], left, simulate, ref changed);

            return left;
        }
        finally
        {
            this.Exit(entry, changed);
        }
    }

    public ItemStack? Extract(int slot, int count, bool simulate = false)
    {
        var index = this.Index(slot);
        if (count <= 0)
            return null;

        if (!this.TryEnter(out var entry))
            return null;

        var changed = false;
        try
        {
            var existing = owner.Inventory[index];
            if (existing is null || existing.IsAir || existing.Count <= 0 || !owner.CanExtract(index, existing, side))
                return null;

            var taken = Math.Min(count, existing.Count);
            var result = new ItemStack(existing, taken);

            if (!simulate)
            {
                var rest = existing.Count - taken;
                owner.Inventory[index] = rest > 0 ? new ItemStack(existing, rest) : null;
                changed = true;
            }

            return result;
        }
        finally
        {
            this.Exit(entry, changed);
        }
    }

    // Puts what fits of a non-empty stack into the inventory's slot, holding its lock; sets changed when it did.
    private ItemStack? InsertHeld(int index, ItemStack stack, bool simulate, ref bool changed)
    {
        if (!owner.CanInsert(index, stack, side))
            return stack;

        var existing = owner.Inventory[index];
        var empty = existing is null || existing.IsAir || existing.Count <= 0;
        // Stack sizes are read without component copies, which may be plugin code reading this container.
        var limit = Math.Clamp(owner.SlotLimit(index, stack), 0, stack.StoredMaxStackSize);

        // Vanilla's HopperBlockEntity.tryMoveInItem: an empty slot takes the stack, a stack of the same item with the
        // same components tops up to the moving stack's size. A slot with no room takes nothing whatever it holds, so
        // its stack isn't compared, which costs more than the rest.
        int moved;
        if (empty)
            moved = Math.Min(stack.Count, limit);
        else if (existing!.Count < limit && existing.Count <= existing.StoredMaxStackSize && existing.Equals(stack))
            moved = Math.Min(limit - existing.Count, stack.Count);
        else
            moved = 0;

        if (moved <= 0)
            return stack;

        if (!simulate)
        {
            owner.OnInserting();
            owner.Inventory[index] = new ItemStack(stack, empty ? moved : existing!.Count + moved);
            changed = true;
        }

        return moved == stack.Count ? null : new ItemStack(stack, stack.Count - moved);
    }

    private int Index(int slot) => (uint)slot < (uint)this.slots.Count
        ? this.slots[slot]
        : throw new ArgumentOutOfRangeException(nameof(slot), slot, null);

    // Takes the inventory's lock once its loot is rolled, or returns false when the container takes and gives
    // nothing: it was removed, its loot can't be rolled now, or (holding the tick lock) a click has it. Release it
    // with Exit.
    private bool TryEnter(out Entry entry)
    {
        entry = default;
        var host = owner.Host as IContainerItemsHost;

        // Rolling loot takes locks that come before a menu's click locks.
        if (withoutWaiting ? owner.LootTable is not null : host is not null && !host.PrepareItems(owner))
            return false;

        var inventory = owner.Inventory;
        var tryOnly = withoutWaiting || host is { HoldsTickLock: true };

        if (inventory.LocksOnlyItself)
        {
            // Taken directly: Lock's ordering and release handles are for several locks, and Exit reports a change.
            var gate = inventory.TransactionLock;
            if (tryOnly)
            {
                if (!gate.Wait(0))
                    return false;
            }
            else
            {
                gate.Wait();
            }

            entry = new Entry(host, gate, null);
        }
        else if (tryOnly)
        {
            // Every container the slots are kept in (a menu's backing containers), or none of them.
            if (!inventory.TryLock(out var held))
                return false;

            entry = new Entry(host, null, held);
        }
        else
        {
            entry = new Entry(host, null, inventory.Lock());
        }

        // Checked again under the lock: the block entity may have gone, or been given a loot table, meanwhile.
        if (owner.IsRemoved || inventory.IsRetired || owner.LootTable is not null)
        {
            this.Exit(entry, changed: false);
            return false;
        }

        return true;
    }

    // Releases what TryEnter took, then tells the server about a change the call made.
    private void Exit(Entry entry, bool changed)
    {
        if (entry.Gate is not null)
            entry.Gate.Release();
        else
            entry.Held?.Dispose();

        if (!changed)
            return;

        owner.SetChanged();
        entry.Host?.OnItemsChanged(owner);
    }

    // The inventory's lock for one call: its own semaphore, or the handle of Lock for one made of several.
    private readonly record struct Entry(IContainerItemsHost? Host, SemaphoreSlim? Gate, IDisposable? Held);
}
