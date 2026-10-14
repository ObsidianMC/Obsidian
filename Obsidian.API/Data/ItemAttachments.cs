using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Utilities;
using Obsidian.Nbt;

namespace Obsidian.API.Data;

/// <summary>
/// Items kept in an attachment, like a player's extra slots for accessories: register an attachment with
/// <see cref="Codec"/> (and <see cref="NetworkCodec"/> to sync it), then reach its slots with
/// <see cref="GetItemAccess"/>, from a menu's <see cref="MenuSlot"/>s or the plugin's own code.
/// </summary>
/// <remarks>
/// <para>
/// The engine knows these attachments hold items. With vanilla's <c>keep_inventory</c> rule off, a player's death drops
/// them where they died, like vanilla drops the inventory, unless the type was registered with <c>copyOnDeath</c>;
/// with the rule on they're kept. Items with the curse of vanishing are destroyed instead of dropped, like vanilla's.
/// </para>
/// <para>
/// The value is the slots' stacks, empty slots null, as long as the highest slot holding something. A get decodes a
/// detached copy, like any attachment's.
/// </para>
/// </remarks>
public static class ItemAttachments
{
    /// <summary>
    /// The most slots an item attachment holds, like vanilla's <c>minecraft:container</c> component.
    /// </summary>
    public const int MaxSlots = 256;

    /// <summary>
    /// Saves the slots like vanilla's <c>minecraft:container</c> component: a list of the slots holding something,
    /// each <c>{ slot, item }</c>. Registering an attachment with it makes it an item attachment.
    /// </summary>
    public static ComponentCodec<ItemStack?[]> Codec { get; } = new(Encode, Decode);

    /// <summary>
    /// Sends the slots to Obsidian clients as a list of optional stacks, like vanilla's container contents. A
    /// client mod registers the same type with <see cref="Codec"/> and this to read them.
    /// </summary>
    public static ComponentNetworkCodec<ItemStack?[]> NetworkCodec { get; } = new(
        (writer, items) =>
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(items.Length, MaxSlots, nameof(items));
            writer.WriteItemStackList(items);
        },
        reader =>
        {
            var count = reader.ReadVarInt();
            if (count is < 0 or > MaxSlots)
                throw new System.IO.InvalidDataException("An item attachment holds at most 256 slots.");

            var items = new ItemStack?[count];
            for (var slot = 0; slot < count; slot++)
                items[slot] = reader.ReadItemStack();

            return items;
        });

    /// <summary>
    /// Access to the first <paramref name="slotCount"/> slots of <paramref name="holder"/>'s item attachment, which
    /// a menu shows with <c>new MenuSlot(access, index)</c>. Each call reads or changes the saved value in one step,
    /// so the attachment stays the only copy of the items: it's saved, synced and kept or dropped at a death as it
    /// is. A change raises <see cref="AttachmentType{TValue}.Changed"/>.
    /// </summary>
    /// <remarks>
    /// Slots take any item, a stack up to its own size; give a menu slot its rules (<see cref="MenuSlot.MayPlace"/>,
    /// <see cref="MenuSlot.MaxStackSize"/>). Once the plugin unloads, or the holder is retired (a player who left),
    /// the access takes and gives nothing. It never waits, so a menu may call it holding its locks.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="type"/> wasn't registered with <see cref="Codec"/>.
    /// </exception>
    public static IItemAccess GetItemAccess(
        this IAttachmentHolder holder,
        AttachmentType<ItemStack?[]> type,
        int slotCount)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slotCount, MaxSlots);

        if (!type.Binding.HoldsItems)
        {
            throw new ArgumentException(
                $"{type.Identifier} isn't registered with ItemAttachments.Codec.", nameof(type));
        }

        return new AttachmentItemAccess(holder, type.Identifier, type.Binding, slotCount);
    }

    internal static INbtTag Encode(ItemStack?[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(items.Length, MaxSlots, nameof(items));

        var list = new NbtList(NbtTagType.Compound, string.Empty);
        for (var slot = 0; slot < items.Length; slot++)
        {
            if (items[slot] is not { IsAir: false, Count: > 0 } item)
                continue;

            list.Add(new NbtCompound
            {
                new NbtTag<int>("slot", slot),
                item.ToNbt("item")
            });
        }

        return list;
    }

    // Unknown items and slots out of range are left out, like vanilla's container contents.
    internal static ItemStack?[] Decode(INbtTag tag)
    {
        if (tag is not NbtList list)
            return [];

        var slots = new SortedDictionary<int, ItemStack>();
        foreach (var entry in list.OfType<NbtCompound>())
        {
            if (entry.TryGetTag<NbtTag<int>>("slot", out var slot)
                && slot.Value is >= 0 and < MaxSlots
                && entry.TryGetTag<NbtCompound>("item", out var saved)
                && saved.ItemFromNbt() is { IsAir: false, Count: > 0 } item)
            {
                slots[slot.Value] = item;
            }
        }

        var items = new ItemStack?[slots.Count == 0 ? 0 : slots.Keys.Max() + 1];
        foreach (var (slot, item) in slots)
            items[slot] = item;

        return items;
    }
}

/// <summary>
/// An item attachment's slots on one holder (see <see cref="ItemAttachments.GetItemAccess"/>). Every call decodes the
/// saved slots and, when it changes them, saves them back, in one step under the holder's lock; it runs no plugin
/// code but the change handlers, after the change.
/// </summary>
internal sealed class AttachmentItemAccess(
    IAttachmentHolder holder,
    string identifier,
    PluginDataBinding binding,
    int slotCount) : IItemAccess
{
    public int SlotCount => slotCount;

    public ItemStack? GetItem(int slot)
    {
        this.CheckSlot(slot);

        using var call = binding.TryEnter();
        if (call is null || !PluginDataBindings.IsCurrent(binding))
            return null;

        var saved = AttachmentStorage.For(holder).Get(identifier);

        return saved is null ? null : At(ItemAttachments.Decode(saved), slot);
    }

    public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
    {
        ArgumentNullException.ThrowIfNull(stack);
        this.CheckSlot(slot);

        if (stack.IsAir || stack.Count <= 0)
            return null;

        var moved = 0;
        var changed = this.Update(simulate, items =>
        {
            // Like a container's access: an empty slot takes the stack, a stack of the same item with the same
            // components tops up to its stack size.
            var existing = At(items, slot);
            moved = existing is null
                ? Math.Min(stack.Count, stack.MaxStackSize)
                : existing.Equals(stack)
                    ? Math.Clamp(existing.MaxStackSize - existing.Count, 0, stack.Count)
                    : 0;

            if (moved <= 0)
                return null;

            items = Grown(items, slot);
            items[slot] = new ItemStack(stack, (existing?.Count ?? 0) + moved);
            return items;
        });

        if (!changed)
            return stack;

        return moved == stack.Count ? null : new ItemStack(stack, stack.Count - moved);
    }

    public ItemStack? Extract(int slot, int count, bool simulate = false)
    {
        this.CheckSlot(slot);

        if (count <= 0)
            return null;

        ItemStack? taken = null;
        this.Update(simulate, items =>
        {
            if (At(items, slot) is not ItemStack existing)
                return null;

            var amount = Math.Min(count, existing.Count);
            taken = new ItemStack(existing, amount);
            items[slot] = existing.Count > amount ? new ItemStack(existing, existing.Count - amount) : null;
            return items;
        });

        return taken;
    }

    // Reads the slots and, unless simulating, saves what change returns (null for no change) in one step, then tells
    // the type's handlers. False when nothing changed or would: the plugin is unloading or the holder retired.
    private bool Update(bool simulate, Func<ItemStack?[], ItemStack?[]?> change)
    {
        using var call = binding.TryEnter();
        if (call is null)
            return false;

        var storage = AttachmentStorage.For(holder);
        var changed = false;

        if (simulate)
        {
            if (!PluginDataBindings.IsCurrent(binding))
                return false;

            var saved = storage.Get(identifier);
            return change(saved is null ? [] : ItemAttachments.Decode(saved)) is not null;
        }

        var published = PluginDataBindings.TryPublish(binding, () => storage.TryUpdate(identifier, saved =>
        {
            var items = change(saved is null ? [] : ItemAttachments.Decode(saved));
            changed = items is not null;

            return items is null ? null : ItemAttachments.Encode(items);
        }));

        if (!published || !changed)
            return false;

        binding.NotifyChanged(holder);
        return true;
    }

    private void CheckSlot(int slot)
    {
        if ((uint)slot >= (uint)slotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
    }

    private static ItemStack? At(ItemStack?[] items, int slot) =>
        slot < items.Length && items[slot] is { IsAir: false, Count: > 0 } item ? item : null;

    private static ItemStack?[] Grown(ItemStack?[] items, int slot)
    {
        if (slot < items.Length)
            return items;

        Array.Resize(ref items, slot + 1);
        return items;
    }
}
