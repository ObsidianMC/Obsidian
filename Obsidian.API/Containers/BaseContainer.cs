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
    /// Takes the container's <see cref="TransactionLock"/>, and those of the containers it's made of, until the
    /// returned handle is disposed.
    /// </summary>
    internal virtual async ValueTask<IDisposable> LockAsync()
    {
        var gate = this.TransactionLock;
        await gate.WaitAsync();

        return new LockRelease(gate);
    }

    /// <summary>
    /// Takes the container's lock like <see cref="LockAsync"/>, blocking, for code that can't wait asynchronously, such
    /// as a save on a chunk's lock. Never call it holding the container's lock.
    /// </summary>
    internal virtual IDisposable Lock()
    {
        var gate = this.TransactionLock;
        gate.Wait();

        return new LockRelease(gate);
    }

    /// <summary>
    /// Takes the locks a click in a player's open <paramref name="window"/> needs: the window's, then the player's
    /// <paramref name="inventory"/>'s, which the window shows too, until the returned handle is disposed. Always in
    /// that order, so that clicks sharing a container, and other changes to the player's inventory, take turns without
    /// deadlocking.
    /// </summary>
    internal static async ValueTask<IDisposable> LockWindowAsync(BaseContainer window, BaseContainer inventory)
    {
        var first = await window.LockAsync();
        if (window.SharesSlotsWith(inventory))
            return first;

        try
        {
            var second = await inventory.LockAsync();

            return new LockPair(first, second);
        }
        catch
        {
            first.Dispose();
            throw;
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
    /// Whether this container and <paramref name="other"/> show the same slots, like a double chest and its halves.
    /// </summary>
    internal virtual bool SharesSlotsWith(BaseContainer other) =>
        ReferenceEquals(this, other)
        || other is Containers.DoubleChestContainer chest && chest.SharesSlotsWith(this);

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
/// Releases two locks taken in turn, the second first.
/// </summary>
internal sealed class LockPair(IDisposable first, IDisposable second) : IDisposable
{
    public void Dispose()
    {
        second.Dispose();
        first.Dispose();
    }
}

public abstract class ResultContainer : BaseContainer
{
    protected ResultContainer(int size) : this(size, InventoryType.Custom) { }

    internal ResultContainer(int size, InventoryType type) : base(size, type) { }

    public abstract void SetResult(ItemStack? result);

    public abstract ItemStack? GetResult();
}
