using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A double chest's 54 slots: <see cref="First"/>'s 27, then <see cref="Second"/>'s, like vanilla's compound
/// container. The halves keep the items; this only shows them together.
/// </summary>
/// <remarks>
/// Each opening makes a new one, so several can show the same halves: they lock both halves, first then second,
/// and share the halves' viewers, so that their clicks take turns with each other's and with a single half's.
/// </remarks>
internal sealed class DoubleChestContainer : BaseContainer
{
    public DoubleChestContainer(BaseContainer first, BaseContainer second, ChatMessage title)
        : base(first.Size + second.Size, InventoryType.Generic)
    {
        this.First = first;
        this.Second = second;
        this.Title = title;
    }

    /// <summary>
    /// The half whose slots come first: the chest whose type is <c>right</c>.
    /// </summary>
    public BaseContainer First { get; }

    public BaseContainer Second { get; }

    // Grows whenever either half does.
    public override long Revision => this.First.Revision + this.Second.Revision;

    public override ItemStack? GetItem(int slot) => this.Half(slot, out var local).GetItem(local);

    public override void SetItem(int slot, ItemStack? item) => this.Half(slot, out var local).SetItem(local, item);

    public override int AddItem(ItemStack item)
    {
        var slot = this.First.AddItem(item);
        if (slot >= 0)
            return slot;

        slot = this.Second.AddItem(item);

        return slot < 0 ? -1 : this.First.Size + slot;
    }

    public override void Resize(int newSize) =>
        throw new NotSupportedException("A double chest has its halves' slots.");

    internal override async ValueTask<IDisposable> LockAsync()
    {
        var first = await this.First.LockAsync();
        try
        {
            var second = await this.Second.LockAsync();

            return new Both(first, second);
        }
        catch
        {
            first.Dispose();
            throw;
        }
    }

    internal override bool SharesSlotsWith(BaseContainer other) =>
        ReferenceEquals(this, other)
        || this.First.SharesSlotsWith(other)
        || this.Second.SharesSlotsWith(other);

    internal override ItemStack?[] GetItems() => [.. this];

    // A viewer of the double chest views both halves, so that clicks in either reach them.
    internal override void AddViewer(IPlayer player)
    {
        base.AddViewer(player);
        this.First.AddViewer(player);
        this.Second.AddViewer(player);
    }

    internal override void RemoveViewer(IPlayer player)
    {
        base.RemoveViewer(player);
        this.First.RemoveViewer(player);
        this.Second.RemoveViewer(player);
    }

    internal override IPlayer[] GetViewers() => [.. this.First.GetViewers().Union(this.Second.GetViewers())];

    private BaseContainer Half(int slot, out int local)
    {
        if (slot < this.First.Size)
        {
            local = slot;

            return this.First;
        }

        local = slot - this.First.Size;

        return this.Second;
    }

    private sealed class Both(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
    }
}
