using Obsidian.API.Inventory;
using Obsidian.API.Utilities;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.API.BlockEntities;

/// <summary>
/// A block entity holding items, like vanilla's chests, barrels, furnaces and brewing stands: its
/// <see cref="Inventory"/> is what players see when they open its block, and is saved like vanilla's containers.
/// </summary>
/// <remarks>
/// <para>
/// Saved like vanilla's <c>RandomizableContainerBlockEntity</c>: <c>CustomName</c>, and the loot table it's waiting
/// for (<c>LootTable</c>, <c>LootTableSeed</c>) or else its <c>Items</c>, each with its <c>Slot</c>. Slots past the
/// inventory's size are skipped. Changes to the inventory are saved without calling
/// <see cref="BlockEntity.SetChanged"/>. Vanilla saves a slot's number as a byte, so an inventory holds at most
/// <see cref="MaxSlots"/> slots.
/// </para>
/// <para>
/// A container with a loot table, like an unopened dungeon chest, is empty until a player opens it, or it's removed,
/// which fills it from the table.
/// </para>
/// <para>
/// Other blocks reach its items through <see cref="GetItemAccess"/>, from every side to every slot unless a subclass
/// overrides <see cref="GetSlots"/>, <see cref="CanInsert"/> and <see cref="CanExtract"/>, like vanilla's
/// <c>WorldlyContainer</c>.
/// </para>
/// <para>
/// A container that derives state from its items overrides <see cref="OnItemsChanged"/>, which the server calls at
/// the end of the level's tick after they changed, rather than comparing its slots every tick.
/// </para>
/// </remarks>
public partial class ContainerBlockEntity : BlockEntity, IItemAccessProvider
{
    /// <summary>
    /// Creates a block entity holding <paramref name="inventory"/>, at most <see cref="MaxSlots"/> slots.
    /// </summary>
    public ContainerBlockEntity(BaseContainer inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (inventory.Size > MaxSlots)
        {
            throw new ArgumentException(
                $"A container block entity holds at most {MaxSlots} slots.", nameof(inventory));
        }

        this.Inventory = inventory;
        inventory.BlockEntityOwner = new(this);
    }

    /// <summary>
    /// The most slots a container block entity holds: vanilla saves each slot's number as a byte.
    /// </summary>
    public const int MaxSlots = 256;

    /// <summary>
    /// The items the block entity holds, which opening its block shows.
    /// </summary>
    public BaseContainer Inventory { get; }

    /// <summary>
    /// The loot table the container is filled from when it's first opened, like
    /// <c>minecraft:chests/simple_dungeon</c>, or null once it was (or when it has none).
    /// </summary>
    public string? LootTable { get; set; }

    /// <summary>
    /// The seed <see cref="LootTable"/> is rolled with; 0 picks a random one.
    /// </summary>
    public long LootTableSeed { get; set; }

    /// <summary>
    /// Whether the items drop when the block is removed, as vanilla's containers' do; shulker boxes keep them.
    /// </summary>
    public bool DropsContents { get; init; } = true;

    /// <summary>
    /// Protects publishing generated loot with its table fields, after taking the inventory's click lock.
    /// </summary>
    internal Lock LootLock { get; } = new();

    /// <summary>Serializes loot preparation outside the tick, item transfer, click and loot locks.</summary>
    internal Lock LootGenerationLock { get; } = new();

    // The inventory's revision when its contents were last saved, to tell when they changed.
    internal long SavedRevision { get; set; } = -1;

    // The inventory's revision when OnItemsChanged was last called; -1 calls it once after the block entity joins
    // its level.
    internal long NotifiedRevision { get; set; } = -1;

    /// <summary>
    /// Called at the end of the level's tick after <see cref="Inventory"/>'s items changed, once however many changes
    /// the tick made, and once after the block entity joins its level (placed, loaded with its chunk, or its plugin
    /// reloaded), following <see cref="BlockEntity.OnLoad"/>. Override it to update what the block entity derives
    /// from its items, like a machine checking the patterns it was given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It hears every change to the items: players' clicks and shift-clicks in any menu showing the inventory, item
    /// access inserting and extracting (hoppers' and other plugins' too), the block entity's own changes under
    /// <see cref="LockInventory"/>, and its loot table filling it. A change made to <see cref="Inventory"/> without
    /// its lock is heard on the ticks of the block entity's chunk. Calling <see cref="BlockEntity.SetChanged"/>
    /// alone doesn't call it.
    /// </para>
    /// <para>
    /// Called inside a call into the block entity's plugin, holding no lock, so it may call
    /// <see cref="LockInventory"/>; a change it makes calls it again on the next tick. Not called once the block
    /// entity was removed or its plugin is unloading. A call that throws is logged.
    /// </para>
    /// </remarks>
    protected internal virtual void OnItemsChanged()
    {
    }

    /// <summary>
    /// Queues <see cref="OnItemsChanged"/> to the end of the level's tick; nothing until the block entity is in a
    /// level. Never waits, so it may be called holding the inventory's lock.
    /// </summary>
    internal void QueueItemsChanged() => (this.Host as IContainerItemsHost)?.QueueItemsChanged(this);

    protected internal override void Load(NbtCompound tag)
    {
        for (var slot = 0; slot < this.Inventory.Size; slot++)
            this.Inventory.SetItem(slot, null);

        this.LootTable = tag.TryGetTagValue<string>("LootTable", out var lootTable) ? lootTable : null;
        this.LootTableSeed = tag.TryGetTagValue<long>("LootTableSeed", out var seed) ? seed : 0;

        // Like vanilla, a container waiting for its loot table has no items yet.
        if (this.LootTable is null && tag.TryGetTag<NbtList>("Items", out var items))
        {
            foreach (var itemTag in items.OfType<NbtCompound>())
            {
                if (!itemTag.TryGetTag<NbtTag<byte>>("Slot", out var slot) || slot.Value >= this.Inventory.Size)
                    continue;

                this.Inventory.SetItem(slot.Value, itemTag.ItemFromNbt());
            }
        }

        if (tag.TryGetTag("CustomName", out var customName) && customName.TextFromNbt() is ChatMessage name)
        {
            this.Inventory.CustomName = name;
            this.Inventory.Title = name;
        }
    }

    /// <remarks>
    /// Saves the inventory under its lock and <see cref="LootLock"/>, so that the save never has a click half applied,
    /// nor a loot table being rolled into the slots (which would save neither the table nor its loot).
    /// </remarks>
    protected internal override void Save(NbtCompound tag)
    {
        using (this.Inventory.Lock())
        {
            lock (this.LootLock)
            this.SaveContents(tag);
        }
    }

    /// <summary>Overlays current container fields without invoking a plugin's Save override.</summary>
    internal void SnapshotContents(NbtCompound tag)
    {
        using (this.Inventory.Lock())
        {
            lock (this.LootLock)
        {
            foreach (var name in new[] { "CustomName", "Items", "LootTable", "LootTableSeed" })
                tag.Remove(name);

            this.SaveContents(tag);
        }
        }
    }

    // What Save writes, under the inventory's lock and the loot lock.
    private void SaveContents(NbtCompound tag)
    {
        this.SaveMachineState(tag);

        if (this.Inventory.CustomName is not null)
            tag.Add(this.Inventory.CustomName.ToNbt("CustomName"));

        if (this.LootTable is not null)
        {
            tag.Add(new NbtTag<string>("LootTable", this.LootTable));

            // Like vanilla, a zero seed isn't saved.
            if (this.LootTableSeed != 0)
                tag.Add(new NbtTag<long>("LootTableSeed", this.LootTableSeed));

            return;
        }

        var items = new NbtList(NbtTagType.Compound, "Items");
        for (var slot = 0; slot < this.Inventory.Size; slot++)
        {
            var item = this.Inventory[slot];
            if (item is null || item.IsAir || item.Count <= 0)
                continue;

            var itemTag = item.ToNbt();
            itemTag.Add(new NbtTag<byte>("Slot", (byte)slot));
            items.Add(itemTag);
        }

        tag.Add(items);
    }

    // Vanilla machines save their counters atomically with their items, under the inventory and loot locks.
    internal virtual void SaveMachineState(NbtCompound tag)
    {
    }
}
