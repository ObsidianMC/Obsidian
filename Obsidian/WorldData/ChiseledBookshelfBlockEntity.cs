using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A chiseled bookshelf's block entity, like vanilla's <c>ChiseledBookShelfBlockEntity</c>: six slots of one book
/// each, and the slot a book last went into or came out of, which comparators read. Its block shows which slots hold a
/// book.
/// </summary>
/// <remarks>
/// Saved as vanilla saves it: <c>Items</c> (always written, empty or not) and <c>last_interacted_slot</c>. Hoppers and
/// droppers reach every slot from every side, as vanilla's plain <c>Container</c> lets them: a book goes into the first
/// empty slot, and the lowest occupied slot comes out first. Like vanilla's <c>setItem</c> and <c>removeItem</c>, each
/// item moved that way updates the block and the last slot at once.
/// </remarks>
internal sealed class ChiseledBookshelfBlockEntity : VanillaContainerBlockEntity
{
    /// <summary>How many books it holds: two rows of three.</summary>
    public const int Slots = 6;

    private const string LastSlotField = "last_interacted_slot";

    private int lastInteractedSlot = -1;

    public ChiseledBookshelfBlockEntity(Vector position)
        : base(new BookshelfContainer { BlockPosition = position }, LastSlotField)
    {
    }

    /// <summary>
    /// The slot a book last went into or came out of, or -1 before any did (vanilla's <c>lastInteractedSlot</c>).
    /// </summary>
    public int LastInteractedSlot => Volatile.Read(ref this.lastInteractedSlot);

    /// <summary>Whether <paramref name="stack"/> is a book a shelf takes: one of <c>#bookshelf_books</c>.</summary>
    public static bool IsBook(ItemStack? stack) =>
        stack is { Count: > 0 } && TagsRegistry.Item.BookshelfBooks.Entries.Contains(stack.Holder.Id);

    /// <summary>Vanilla's <c>getAnalogOutputSignal</c>: the last slot used, plus one.</summary>
    public int AnalogSignal() => this.LastInteractedSlot + 1;

    /// <summary>
    /// Puts one of <paramref name="book"/> into the empty <paramref name="slot"/> and shows it on the block. Call
    /// holding the inventory's lock.
    /// </summary>
    /// <returns>False when the slot holds a book already, or it isn't a book.</returns>
    public bool TryPut(int slot, ItemStack book)
    {
        if (!IsBook(book) || !this.Inventory.GetItem(slot).IsNullOrAir())
            return false;

        this.Inventory.SetItem(slot, new ItemStack(book, 1));
        this.ShowSlots(slot);
        return true;
    }

    /// <summary>
    /// Takes the book out of <paramref name="slot"/> and shows it gone on the block. Call holding the inventory's
    /// lock.
    /// </summary>
    /// <returns>The book, or null when the slot is empty.</returns>
    public ItemStack? TryTake(int slot)
    {
        if (this.Inventory.GetItem(slot) is not { Count: > 0, IsAir: false } book)
            return null;

        this.Inventory.SetItem(slot, null);
        this.ShowSlots(slot);
        return book;
    }

    public override IItemAccess? GetItemAccess(BlockFace? side) =>
        base.GetItemAccess(side) is IItemAccess access ? new ShelfItemAccess(this, access) : null;

    // Vanilla's canPlaceItem: a book, into an empty slot.
    protected internal override bool CanInsert(int slot, ItemStack stack, BlockFace? side) =>
        IsBook(stack) && this.Inventory.GetItem(slot).IsNullOrAir();

    // Vanilla's getMaxStackSize: one book a slot.
    protected internal override int SlotLimit(int slot, ItemStack stack) => 1;

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);

        var last = tag.TryGetNumber<int>(LastSlotField, out var saved) ? saved : -1;
        Volatile.Write(ref this.lastInteractedSlot, last);
    }

    protected internal override void Save(NbtCompound tag)
    {
        base.Save(tag);

        tag.Add(new NbtTag<int>(LastSlotField, this.LastInteractedSlot));
    }

    /// <summary>
    /// Vanilla's <c>updateState</c>: remembers <paramref name="slot"/> as the last used, shows on the block which slots
    /// hold a book, with every update, and emits a <c>block_change</c> game event without a source.
    /// </summary>
    private void ShowSlots(int slot)
    {
        Volatile.Write(ref this.lastInteractedSlot, slot);
        this.SetChanged();

        if (this.Level is not AbstractLevel level)
            return;

        var position = this.BlockPosition;
        lock (level.Fluids.TickLock)
        {
            var updates = level.BlockUpdates;
            var state = updates.GetBlock(position);
            if (state.RegistryId != BlockIds.ChiseledBookshelf)
                return;

            var shown = state;
            for (var index = 0; index < Slots; index++)
            {
                var occupied = !this.Inventory.GetItem(index).IsNullOrAir();
                shown = shown.WithProperty($"slot_{index}_occupied", occupied);
            }

            updates.SetBlock(position, shown);
            updates.UpdateAnalogOutput(position);
        }

        level.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)position + new VectorD(0.5, 0.5, 0.5));
    }

    /// <summary>The shelf's six slots, which have no menu.</summary>
    private sealed class BookshelfContainer() : BaseContainer(Slots, InventoryType.Generic)
    {
        public Vector BlockPosition { get; set; }
    }

    /// <summary>
    /// The shelf's item access, which shows each book that moves on the block once the move is done and the
    /// inventory's lock is released.
    /// </summary>
    private sealed class ShelfItemAccess(ChiseledBookshelfBlockEntity shelf, IItemAccess slots) : IItemAccess
    {
        public int SlotCount => slots.SlotCount;

        public ItemStack? GetItem(int slot) => slots.GetItem(slot);

        public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
        {
            var rest = slots.Insert(slot, stack, simulate);
            var moved = rest is null || rest.Count < stack.Count;
            if (moved && !simulate)
                shelf.ShowMoved(slot);

            return rest;
        }

        public ItemStack? Extract(int slot, int count, bool simulate = false)
        {
            var taken = slots.Extract(slot, count, simulate);
            if (taken is not null && !simulate)
                shelf.ShowMoved(slot);

            return taken;
        }
    }

    // The shelf's own slots are its item access slots, in order.
    private void ShowMoved(int slot)
    {
        if (!this.IsRemoved)
            this.ShowSlots(slot);
    }
}
