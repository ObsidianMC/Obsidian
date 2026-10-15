using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using System.Collections;
using System.Threading;

namespace Obsidian.API;

public abstract class BaseContainer : IEnumerable<ItemStack>
{
    protected ItemStack?[] items;

    public short Size => (short)this.items.Length;

    public InventoryType Type { get; }

    public ChatMessage Title { get; set; }

    /// <summary>
    /// The name the container was given (vanilla's <c>CustomName</c>), shown as its <see cref="Title"/> instead of the
    /// default one; <c>null</c> when it has none.
    /// </summary>
    public ChatMessage? CustomName { get; set; }

    public Guid Uuid { get; } = Guid.NewGuid();

    // A retained inventory must not keep an unloaded plugin's block entity or code alive.
    internal WeakReference<ContainerBlockEntity>? BlockEntityOwner { get; set; }

    /// <summary>Combines the block entity's slot limit with the item's own stack size, under the click lock.</summary>
    internal int SlotLimit(int slot, ItemStack stack) =>
        this.BlockEntityOwner is not null && this.BlockEntityOwner.TryGetTarget(out var owner)
            ? Math.Clamp(owner.MenuSlotLimit(slot, stack), 0, stack.MaxStackSize)
            : stack.MaxStackSize;

    /// <summary>
    /// The players who have the container open. The server keeps it as they open and close it.
    /// </summary>
    public List<IPlayer> Viewers { get; } = [];

    private SemaphoreSlim? transactionLock;

    // The stamp of the last time a slot was set (see MarkChanged); the stacks keep their own (see Revision).
    private long slotsChanged;

    // 1 once the container is retired (see IsRetired).
    private int retired;

    /// <summary>
    /// Taken while a player's click changes the container, so that the clicks of players sharing it take turns.
    /// Take it with <see cref="LockAsync"/>, which also covers containers made of others.
    /// </summary>
    internal SemaphoreSlim TransactionLock =>
        LazyInitializer.EnsureInitialized(ref this.transactionLock, () => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Grows whenever the container's contents change: a slot set, emptied or added to through the container's
    /// methods, or a stack it holds changing its count or components (see <see cref="ChangeStamp"/>). A click
    /// remembers it as it arrives, and the server applies the click only if it hasn't moved since, so that the click,
    /// and what plugins' handlers decided about it, apply to the contents they saw.
    /// </summary>
    /// <remarks>
    /// Component values are immutable. Assign a replacement through the stack's component indexer, for example
    /// <c>stack[food.Type] = food with { Nutrition = 5 }</c>; the slot's revision then advances automatically.
    /// </remarks>
    public virtual long Revision
    {
        get
        {
            var revision = Volatile.Read(ref this.slotsChanged);

            foreach (var item in this.items)
            {
                if (item is not null)
                    revision = Math.Max(revision, item.Version);
            }

            return revision;
        }
    }

    public ItemStack? this[int index] { get => this.GetItem(index); set => this.SetItem(index, value); }

    public ReadOnlySpan<ItemStack?> this[Range range] => this.GetItems().AsSpan(range);

    public BaseContainer(int size) : this(size, InventoryType.Custom) { }

    internal BaseContainer(int size, InventoryType type)
    {
        this.Type = type;

        this.items = new ItemStack?[size];
    }

    public virtual int AddItem(ItemStack item)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (int i = 0; i < this.Size; i++)
        {
            var invItem = this.GetItem(i);

            if (invItem is not null && invItem.Holder == item.Holder)
            {
                //TODO use the items max stack size
                if (invItem.Count >= 64)
                    continue;

                invItem.Count += item.Count;
                this.SetItem(i, invItem);

                return i;
            }

            if (invItem == null)
            {
                this.SetItem(i, item);

                return i;
            }
        }

        return -1;
    }

    public virtual void SetItem(int slot, ItemStack? item)
    {
        this.items[slot] = item;
        this.MarkChanged();
    }

    public virtual ItemStack? GetItem(int slot) => this.items[slot];

    public virtual bool RemoveItem(int slot)
    {
        if (this.GetItem(slot) == null)
            return false;

        this.SetItem(slot, null);

        return true;
    }

    public virtual bool RemoveItem(int slot, int amount)
    {
        var item = this.GetItem(slot);

        if (item == null)
            return false;

        item.Count -= amount;
        this.SetItem(slot, item.Count <= 0 ? null : item);

        return true;
    }

    public virtual bool RemoveItem(int slot, out ItemStack? removedItem)
    {
        var item = this.GetItem(slot);

        if (item == null)
        {
            removedItem = null;
            return false;
        }

        removedItem = item;

        return this.RemoveItem(slot);
    }

    public virtual bool RemoveItem(int slot, int amount, out ItemStack? removedItem)
    {
        var item = this.GetItem(slot);

        if (item == null)
        {
            removedItem = null;
            return false;
        }

        removedItem = item;

        return this.RemoveItem(slot, amount);
    }

    /// <summary>
    /// Gets the slot difference for the given clicked slot.
    /// </summary>
    /// <param name="clickedSlot">The slot that was clicked</param>
    /// <returns>
    /// A SlotDifference containing the resolved slot and whether it maps to the player's inventory.
    /// </returns>
    /// <remarks>
    /// This method is used to determine the actual slot number for a clicked slot in a container.
    /// </remarks>
    public virtual SlotDifference GetSlot(short clickedSlot) =>
        clickedSlot >= this.Size ? new((short)(clickedSlot - this.Size + 9), true) : new(clickedSlot, false);

    public virtual void Resize(int newSize)
    {
        Array.Resize(ref this.items, newSize);
        this.MarkChanged();
    }

    /// <summary>
    /// Records a change to the container's slots (see <see cref="Revision"/>).
    /// </summary>
    internal void MarkChanged() => Volatile.Write(ref this.slotsChanged, ChangeStamp.Next());

    /// <summary>
    /// The containers whose <see cref="TransactionLock"/>s guard this one's slots: itself, or the containers it's made
    /// of.
    /// </summary>
    internal virtual IEnumerable<BaseContainer> LockParts => [this];

    /// <summary>
    /// Takes the container's <see cref="TransactionLock"/>, and those of the containers it's made of, until the
    /// returned handle is disposed.
    /// </summary>
    internal ValueTask<IDisposable> LockAsync() => LockAllAsync(this.LockParts);

    /// <summary>
    /// Takes the container's lock like <see cref="LockAsync"/>, blocking, for code that can't wait asynchronously,
    /// such as a save on a chunk's lock. Never call it holding the container's lock.
    /// </summary>
    internal IDisposable Lock()
    {
        var parts = Ordered(this.LockParts);
        var taken = new List<IDisposable>(parts.Length);

        foreach (var part in parts)
        {
            part.TransactionLock.Wait();
            taken.Add(new PartRelease(part));
        }

        return new LockSet(taken);
    }

    /// <summary>
    /// Takes the locks a click in a player's open <paramref name="window"/> needs: the window's and the player's
    /// <paramref name="inventory"/>'s, which the window shows too, until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// Every lock of several containers takes them in one order (see <see cref="Ordered"/>), so that clicks sharing
    /// containers, plugins' menus made of several, and other changes to the player's inventory take turns without
    /// deadlocking.
    /// </remarks>
    internal static ValueTask<IDisposable> LockWindowAsync(BaseContainer window, BaseContainer inventory) =>
        LockAllAsync(window.LockParts.Append(inventory));

    // Takes the containers' locks in order; the returned handle releases them in reverse.
    private static async ValueTask<IDisposable> LockAllAsync(IEnumerable<BaseContainer> parts)
    {
        var ordered = Ordered(parts);
        var taken = new List<IDisposable>(ordered.Length);

        try
        {
            foreach (var part in ordered)
            {
                await part.TransactionLock.WaitAsync();
                taken.Add(new PartRelease(part));
            }
        }
        catch
        {
            new LockSet(taken).Dispose();
            throw;
        }

        return new LockSet(taken);
    }

    // The distinct containers in the order every lock of several takes their locks: by a rank each container gets the
    // first time it's locked, which never changes.
    private static BaseContainer[] Ordered(IEnumerable<BaseContainer> parts) =>
    [
        .. parts
            .Distinct()
            .OrderBy(part => part.LockRank)
    ];

    /// <summary>
    /// Releases one container's lock taken by <see cref="Lock"/> or <see cref="LockAsync"/>. A block entity's
    /// inventory whose items changed while it was held then queues the block entity's
    /// <see cref="ContainerBlockEntity.OnItemsChanged"/>, so that clicks, loot and item access are all heard.
    /// </summary>
    private sealed class PartRelease : IDisposable
    {
        private readonly BaseContainer part;
        private readonly ContainerBlockEntity? owner;
        private readonly long revision;
        private int released;

        // Created holding the part's lock, so that the revision is the one the holder starts from.
        public PartRelease(BaseContainer part)
        {
            this.part = part;

            if (part.BlockEntityOwner is not null && part.BlockEntityOwner.TryGetTarget(out var owner))
            {
                this.owner = owner;
                this.revision = part.Revision;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.released, 1) != 0)
                return;

            // Read before releasing, so that the change counted is the holder's own.
            var changed = this.owner is not null && this.part.Revision != this.revision;
            this.part.TransactionLock.Release();

            if (changed)
                this.owner?.QueueItemsChanged();
        }
    }

    private static long nextLockRank;

    private long lockRank;

    // The container's place in the order of Ordered, given when it's first asked for.
    private long LockRank
    {
        get
        {
            if (Volatile.Read(ref this.lockRank) == 0)
                Interlocked.CompareExchange(ref this.lockRank, Interlocked.Increment(ref nextLockRank), 0);

            return Volatile.Read(ref this.lockRank);
        }
    }

    /// <summary>
    /// Whether the container was retired: its block entity was removed, or its plugin unloaded. Clicks in a window
    /// showing it aren't applied and it doesn't open again, so that nothing more moves in or out of it.
    /// </summary>
    internal bool IsRetired => Volatile.Read(ref this.retired) == 1;

    /// <summary>
    /// Whether the window shows a retired container: this one, or one it's made of.
    /// </summary>
    internal virtual bool ShowsRetired => this.IsRetired;

    /// <summary>
    /// Retires the container (see <see cref="IsRetired"/>). Call it holding the container's lock (see
    /// <see cref="LockAsync"/>), so that no click is half applied.
    /// </summary>
    /// <returns>Whether this call retired it: false when it was already.</returns>
    internal bool TryRetire() => Interlocked.Exchange(ref this.retired, 1) == 0;

    // Only the aborted break that acquired retirement may undo it, under the click lock.
    internal void CancelRetirement() => Volatile.Write(ref this.retired, 0);

    /// <summary>
    /// Whether this container and <paramref name="other"/> show the same slots, like a double chest and its halves, or
    /// a plugin's menu and the containers its slots are kept in.
    /// </summary>
    internal virtual bool SharesSlotsWith(BaseContainer other) =>
        ReferenceEquals(this, other)
        || other is Containers.DoubleChestContainer or MenuContainer && other.SharesSlotsWith(this);

    /// <summary>
    /// The container's slots in order: its own array, or a copy for a container made of others.
    /// </summary>
    internal virtual ItemStack?[] GetItems() => this.items;

    internal virtual void AddViewer(IPlayer player)
    {
        lock (this.Viewers)
        {
            if (!this.Viewers.Contains(player))
                this.Viewers.Add(player);
        }
    }

    internal virtual void RemoveViewer(IPlayer player)
    {
        lock (this.Viewers)
            this.Viewers.Remove(player);
    }

    /// <summary>
    /// Copies <see cref="Viewers"/>, which players opening and closing the container change meanwhile.
    /// </summary>
    internal virtual IPlayer[] GetViewers()
    {
        lock (this.Viewers)
            return [.. this.Viewers];
    }

    public bool HasItems() => this.Any(x => x is not null);

    public IEnumerator<ItemStack> GetEnumerator()
    {
        for (var slot = 0; slot < this.Size; slot++)
            yield return this.GetItem(slot)!;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public readonly record struct SlotDifference(short Slot, bool ForPlayer);

/// <summary>
/// Releases a container's lock (see <see cref="BaseContainer.LockAsync"/>) once.
/// </summary>
internal sealed class LockRelease(SemaphoreSlim gate) : IDisposable
{
    private int released;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.released, 1) == 0)
            gate.Release();
    }
}

/// <summary>
/// Releases locks taken in turn, the last first.
/// </summary>
internal sealed class LockSet(List<IDisposable> taken) : IDisposable
{
    public void Dispose()
    {
        for (var index = taken.Count - 1; index >= 0; index--)
            taken[index].Dispose();
    }
}

public abstract class ResultContainer : BaseContainer
{
    protected ResultContainer(int size) : this(size, InventoryType.Custom) { }

    internal ResultContainer(int size, InventoryType type) : base(size, type) { }

    public abstract void SetResult(ItemStack? result);

    public abstract ItemStack? GetResult();
}
