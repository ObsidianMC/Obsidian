using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Nbt;
using Obsidian.Net;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A decorated pot's block entity, like vanilla's <c>DecoratedPotBlockEntity</c>: one stack of one item, put in by
/// hand or by a hopper and taken out by a hopper, and the sherds on its faces.
/// </summary>
/// <remarks>
/// Saved as vanilla saves it: <c>sherds</c> (the faces, as they were loaded or placed) and <c>item</c> (one stack, no
/// slot), or the loot table it waits for instead of the item. Comparators read how full its one slot is. Its item
/// drops when it's removed, and its block's loot copies the sherds.
/// </remarks>
internal sealed class DecoratedPotBlockEntity : VanillaContainerBlockEntity
{
    // The wobble block event's type (vanilla's EVENT_POT_WOBBLES); its data is the style.
    private const int WobbleEvent = 1;

    private const string ItemField = "item";

    private const string SherdsField = "sherds";

    private INbtTag? sherds;

    public DecoratedPotBlockEntity(Vector position)
        : base(new PotContainer { BlockPosition = position }, ItemField, SherdsField)
    {
    }

    /// <summary>
    /// How a pot wobbles when it's used, like vanilla's <c>WobbleStyle</c>: the block event's data is its ordinal.
    /// </summary>
    public enum Wobble
    {
        /// <summary>An item went in.</summary>
        Positive,

        /// <summary>Nothing went in.</summary>
        Negative
    }

    /// <summary>
    /// The saved <c>sherds</c>: vanilla's compound of faces, or the list of item ids older worlds saved, or null for a
    /// pot without sherds. A copy; set it to replace it.
    /// </summary>
    public INbtTag? Sherds
    {
        get => Volatile.Read(ref this.sherds) is INbtTag saved ? DataBlockEntity.Copy(saved) : null;
        set => Volatile.Write(ref this.sherds, value is null ? null : DataBlockEntity.Copy(value));
    }

    /// <summary>
    /// Whether one of <paramref name="stack"/> fits: the pot is empty, or holds the same item with the same components
    /// and less than a full stack, like vanilla's <c>DecoratedPotBlock.useItemOn</c>. Call holding the inventory's
    /// lock to act on the answer.
    /// </summary>
    public bool Accepts(ItemStack stack)
    {
        var held = this.Inventory.GetItem(0);
        if (held.IsNullOrAir() || held!.Count <= 0)
            return true;

        return held.Equals(stack) && held.Count < held.MaxStackSize;
    }

    /// <summary>
    /// Puts one of <paramref name="stack"/> in, when it fits. Call holding the inventory's lock.
    /// </summary>
    /// <param name="stack">The stack one goes in from.</param>
    /// <param name="count">How many the pot holds after.</param>
    /// <returns>False when it doesn't fit.</returns>
    public bool TryPut(ItemStack stack, out int count)
    {
        count = 0;
        if (stack.Count <= 0 || !this.Accepts(stack))
            return false;

        var held = this.Inventory.GetItem(0);
        count = held.IsNullOrAir() ? 1 : held!.Count + 1;
        this.Inventory.SetItem(0, new ItemStack(stack, count));
        this.SetChanged();
        return true;
    }

    /// <summary>
    /// Vanilla's <c>DecoratedPotBlock.playerWillDestroy</c>: the state a player breaking <paramref name="pot"/> with
    /// <paramref name="tool"/> leaves it in first. A tool in <c>#breaks_decorated_pots</c> without an enchantment in
    /// <c>#prevents_decorated_pot_shattering</c> cracks it, so its loot is its sherds; anything else leaves it whole.
    /// </summary>
    /// <returns>The cracked pot, or null when the pot stays as it is.</returns>
    public static IBlock? CrackedBy(IBlock pot, ItemStack? tool)
    {
        if (!pot.Is(BlockIds.DecoratedPot) || tool is not { Count: > 0 })
            return null;

        if (!TagsRegistry.Item.BreaksDecoratedPots.Entries.Contains(tool.Holder.Id))
            return null;

        var shatterproof = EnchantmentHelper.GetEnchantments(tool).Any(enchantment => enchantment.Level > 0
            && TagsRegistry.Enchantment.PreventsDecoratedPotShattering.Entries.Contains(enchantment.Id));
        if (shatterproof)
            return null;

        return pot.WithProperty("cracked", true);
    }

    /// <summary>Wobbles the pot, like vanilla's <c>wobble</c>: a block event the players around see.</summary>
    public void Shake(Wobble style)
    {
        if (this.Level is AbstractLevel level)
            level.QueueBlockEvent(this.BlockPosition, BlockIds.DecoratedPot, WobbleEvent, (int)style);
    }

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);

        var loaded = tag.TryGetTag(SherdsField, out var saved) ? saved : null;
        this.Sherds = loaded;

        // Like vanilla, a pot waiting for its loot table has no item.
        if (this.LootTable is null
            && tag.TryGetTag<NbtCompound>(ItemField, out var item)
            && item.ItemFromNbt() is { IsAir: false, Count: > 0 } stack)
        {
            this.Inventory.SetItem(0, stack);
        }
    }

    protected internal override void Save(NbtCompound tag)
    {
        base.Save(tag);

        // Vanilla saves the one stack on its own, not as a container's items.
        tag.Remove("Items");
        if (this.LootTable is null && this.Inventory.GetItem(0) is { IsAir: false, Count: > 0 } item)
            tag.Add(item.ToNbt(ItemField));

        if (this.Sherds is INbtTag faces)
            tag.Add(faces);
    }

    // Vanilla's getUpdateTag, saveCustomOnly: the sherds and the item, which clients draw the pot with. The loot table
    // stays on the server.
    protected internal override NbtCompound? GetUpdateData()
    {
        var data = new NbtCompound();
        if (this.Sherds is INbtTag faces)
            data.Add(faces);

        if (this.Inventory.GetItem(0) is { IsAir: false, Count: > 0 } item)
            data.Add(item.ToNbt(ItemField));

        NetworkBuffer.ProjectUpdateItems(data);
        return data.Count == 0 ? null : data;
    }

    /// <summary>The pot's one slot, which has no menu.</summary>
    private sealed class PotContainer() : BaseContainer(1, InventoryType.Generic)
    {
        public Vector BlockPosition { get; set; }
    }
}
