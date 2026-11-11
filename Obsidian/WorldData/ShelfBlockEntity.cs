using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A shelf's block entity, like vanilla's <c>ShelfBlockEntity</c>: a row of three slots of a whole stack each, which a
/// player's use swaps with their hand (or, while the shelf is powered, with the end of their hotbar), and hoppers reach
/// from every side.
/// </summary>
/// <remarks>
/// Saved as vanilla saves it: <c>Items</c> (always written, empty or not) and <c>align_items_to_bottom</c>, which
/// clients get too. Comparators behind the shelf read a bit for each slot that holds something. Every change saves the
/// shelf, sends clients its items, tells the comparators around and, like vanilla's <c>setChanged</c>, emits a game
/// event at its centre without a source.
/// </remarks>
internal sealed class ShelfBlockEntity : VanillaContainerBlockEntity
{
    /// <summary>How many slots a shelf has: one row of three.</summary>
    public const int Slots = 3;

    private const string AlignField = "align_items_to_bottom";

    // Vanilla's hopper transfer calls the shelf's setChanged three times for an item put in (setItem, then the
    // transfer's and the hopper's own), and twice for one taken out (removeItem, then the hopper's).
    private const int InsertChanges = 3;

    private const int ExtractChanges = 2;

    private int alignItemsToBottom;

    public ShelfBlockEntity(Vector position)
        : base(new ShelfContainer { BlockPosition = position }, AlignField)
    {
    }

    /// <summary>
    /// Vanilla's <c>align_items_to_bottom</c>: whether clients draw the items at the bottom of the shelf. Nothing on
    /// the server changes it; it's kept as it was loaded.
    /// </summary>
    public bool AlignItemsToBottom => Volatile.Read(ref this.alignItemsToBottom) != 0;

    /// <summary>
    /// Vanilla's <c>getAnalogOutputSignal</c> for the shelf: a bit for each slot holding something, the first slot's
    /// the lowest. Only a comparator behind the shelf reads it (see <see cref="AnalogSignal(BlockFace, string?)"/>).
    /// </summary>
    public int AnalogSignal()
    {
        var signal = 0;
        for (var slot = 0; slot < Slots; slot++)
        {
            if (!this.Inventory.GetItem(slot).IsNullOrAir())
                signal |= 1 << slot;
        }

        return signal;
    }

    /// <summary>
    /// What a comparator reading the shelf towards <paramref name="direction"/> (from the shelf to the comparator)
    /// gets: <see cref="AnalogSignal()"/> behind a shelf facing <paramref name="facing"/>, and 0 anywhere else.
    /// </summary>
    public int AnalogSignal(BlockFace direction, string? facing)
    {
        var behind = Features.FeatureHelpers.ParseFace(facing).Opposite();
        return behind == direction ? this.AnalogSignal() : 0;
    }

    /// <summary>
    /// Vanilla's <c>swapItemNoUpdate</c>: puts <paramref name="stack"/> (or nothing) into <paramref name="slot"/> and
    /// returns what was there. Call holding the inventory's lock, then <see cref="Changed"/> once the lock is released.
    /// </summary>
    public ItemStack? Swap(int slot, ItemStack? stack)
    {
        var previous = this.Inventory.GetItem(slot);
        var stored = stack.IsNullOrAir() || stack!.Count <= 0 ? null : stack;
        this.Inventory.SetItem(slot, stored);

        return previous.IsNullOrAir() || previous!.Count <= 0 ? null : previous;
    }

    /// <summary>
    /// Vanilla's <c>setChanged(event)</c>: the shelf saves, clients get its items, the comparators around read it
    /// again, and <paramref name="gameEvent"/> (when there is one) happens at its centre without a source. Call
    /// holding no inventory's lock.
    /// </summary>
    public void Changed(MobGameEvent? gameEvent)
    {
        this.SetChanged();
        this.SendUpdate();

        if (this.IsRemoved || this.Level is not AbstractLevel level)
            return;

        var center = (VectorD)this.BlockPosition + new VectorD(0.5, 0.5, 0.5);
        if (gameEvent is MobGameEvent kind)
            level.EmitGameEvent(kind, center);

        level.UpdateComparatorsOf(this);
    }

    public override IItemAccess? GetItemAccess(BlockFace? side) =>
        base.GetItemAccess(side) is IItemAccess access ? new ShelfItemAccess(this, access) : null;

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);

        var align = tag.TryGetBool(AlignField, out var saved) && saved;
        Volatile.Write(ref this.alignItemsToBottom, align ? 1 : 0);
    }

    protected internal override void Save(NbtCompound tag)
    {
        base.Save(tag);

        tag.Add(new NbtTag<bool>(AlignField, this.AlignItemsToBottom));
    }

    // Vanilla's getUpdateTag: the items, always written, and how they're drawn.
    protected internal override NbtCompound? GetUpdateData()
    {
        var data = new NbtCompound();
        using (InventorySnapshot.Lock(this.Inventory))
            data.Add(InventorySnapshot.SaveItems(this.Inventory));

        data.Add(new NbtTag<bool>(AlignField, this.AlignItemsToBottom));
        NetworkBuffer.ProjectUpdateItems(data);
        return data;
    }

    /// <summary>The shelf's three slots, which have no menu.</summary>
    private sealed class ShelfContainer() : BaseContainer(Slots, InventoryType.Generic)
    {
        public Vector BlockPosition { get; set; }
    }

    /// <summary>
    /// The shelf's item access, which runs the shelf's change once a move is done and the inventory's lock is released,
    /// as vanilla's hopper transfer runs its <c>setChanged</c>: each a <c>block_activate</c> game event.
    /// </summary>
    private sealed class ShelfItemAccess(ShelfBlockEntity shelf, IItemAccess slots) : IItemAccess
    {
        public int SlotCount => slots.SlotCount;

        public ItemStack? GetItem(int slot) => slots.GetItem(slot);

        public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
        {
            var rest = slots.Insert(slot, stack, simulate);
            var moved = rest is null || rest.Count < stack.Count;
            if (moved && !simulate)
                shelf.Moved(InsertChanges);

            return rest;
        }

        public ItemStack? Extract(int slot, int count, bool simulate = false)
        {
            var taken = slots.Extract(slot, count, simulate);
            if (taken is not null && !simulate)
                shelf.Moved(ExtractChanges);

            return taken;
        }
    }

    private void Moved(int changes)
    {
        for (var change = 0; change < changes; change++)
            this.Changed(MobGameEvent.BlockActivate);
    }
}
