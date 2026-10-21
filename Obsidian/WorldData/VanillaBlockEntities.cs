using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// The classes of vanilla's block entity types the server models: containers players open, and brewing stands.
/// Vanilla's other types are <see cref="DataBlockEntity"/>s.
/// </summary>
internal static class VanillaBlockEntities
{
    /// <summary>
    /// A new block entity of one of vanilla's types the server models, at <paramref name="position"/>, or null for
    /// the others.
    /// </summary>
    public static BlockEntity? Create(string type, Vector position) => type switch
    {
        "minecraft:chest" or "minecraft:trapped_chest" => Chest(new Container { Title = "Chest" }, position),
        "minecraft:barrel" => Chest(new Container { Title = "Barrel" }, position),
        "minecraft:shulker_box" => new ShulkerBoxBlockEntity(new Container
        {
            Title = "Shulker Box",
            BlockPosition = position
        })
        {
            // Like vanilla, its items stay in the box, which drops as an item holding them.
            DropsContents = false
        },
        "minecraft:dispenser" => Chest(new Container(9) { Title = "Dispenser" }, position),
        "minecraft:dropper" => Chest(new Container(9) { Title = "Dropper" }, position),
        "minecraft:hopper" => new HopperBlockEntity(position),
        "minecraft:furnace" => Smelting(InventoryType.Furnace, "furnace", "Furnace", position),
        "minecraft:blast_furnace" => Smelting(InventoryType.BlastFurnace, "blast_furnace", "Blast Furnace", position),
        "minecraft:smoker" => Smelting(InventoryType.Smoker, "smoker", "Smoker", position),
        "minecraft:campfire" => new CampfireBlockEntity(),
        "minecraft:brewing_stand" => new BrewingStandBlockEntity(position),
        "minecraft:comparator" => new Redstone.ComparatorBlockEntity(),
        "minecraft:beacon" => new BeaconBlockEntity(),
        "minecraft:lectern" => new LecternBlockEntity(position),
        "minecraft:crafter" => new CrafterBlockEntity(position),
        _ => null
    };

    // The container's position is where closing it animates the block.
    private static VanillaContainerBlockEntity Chest(Container container, Vector position)
    {
        container.BlockPosition = position;

        return new VanillaContainerBlockEntity(container);
    }

    // Each furnace type uses the same inventory shape, with its own recipe type and fuel speed.
    private static FurnaceBlockEntity Smelting(InventoryType type, string id, string title, Vector position) =>
        new FurnaceBlockEntity(new SmeltingContainer(type, id)
        {
            Title = title,
            BlockPosition = position
        });
}

/// <summary>
/// One of vanilla's container block entities. Unmodelled fields of its saved data are saved as they were loaded.
/// </summary>
/// <param name="inventory">The items it holds.</param>
/// <param name="modelled">The saved fields a subclass reads and writes itself.</param>
internal class VanillaContainerBlockEntity(BaseContainer inventory, params string[] modelled)
    : ContainerBlockEntity(inventory)
{
    // The fields ContainerBlockEntity reads and writes.
    private static readonly string[] containerFields = ["CustomName", "Items", "LootTable", "LootTableSeed"];

    private NbtCompound others = new();

    // Players change the inventory from their connections, so it's saved as it is whenever its chunk is.
    internal override bool SavesOnAnyThread => true;

    /// <summary>
    /// The container's lock, like vanilla's <c>LockCode</c> saved as <c>lock</c>: the item predicate that opens it,
    /// or null for none. A copy; set it to replace it.
    /// </summary>
    internal NbtCompound? LockPredicate
    {
        get => Volatile.Read(ref this.others).TryGetTag<NbtCompound>("lock", out var key)
            ? DataBlockEntity.Copy(key)
            : null;
        set
        {
            // Replaced whole, since saves read the fields from other threads.
            var others = DataBlockEntity.Copy(Volatile.Read(ref this.others));
            others.Remove("lock");

            if (value is not null)
                others.Add("lock", DataBlockEntity.Copy(value));

            Volatile.Write(ref this.others, others);
        }
    }

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);

        var others = new NbtCompound();
        foreach (var (name, child) in tag)
        {
            if (!containerFields.Contains(name) && !modelled.Contains(name))
                others.Add(name, child);
        }

        this.others = DataBlockEntity.Copy(others);
    }

    protected internal override void Save(NbtCompound tag)
    {
        foreach (var (name, child) in DataBlockEntity.Copy(this.others))
            tag.Add(name, child);

        base.Save(tag);
    }
}

/// <summary>
/// A brewing stand's block entity, which brews in its <see cref="BrewingStand"/> while its chunk ticks.
/// </summary>
internal sealed partial class BrewingStandBlockEntity : VanillaContainerBlockEntity
{
    public BrewingStandBlockEntity(Vector position)
        : base(new BrewingStand { BlockPosition = position }, "BrewTime", "Fuel")
    {
    }

    public BrewingStand Stand => (BrewingStand)this.Inventory;

    // Vanilla's BrewingStandBlockEntity.loadAdditional: the brew's time left and the fuel left.
    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);

        var stand = this.Stand;
        stand.BrewTime = tag.TryGetTagValue<short>("BrewTime", out var brewTime) ? brewTime : 0;
        stand.Fuel = tag.TryGetTagValue<byte>("Fuel", out var fuel) ? fuel : 0;

        // A brew in progress takes its unsaved ingredient from the ingredient slot, so it goes on after a reload
        // instead of stopping and spending another fuel use.
        stand.BrewingIngredient = stand.BrewTime > 0 ? stand.GetItem(BrewingStand.IngredientSlot)?.Holder : null;
    }

    protected internal override void Save(NbtCompound tag)
    {
        base.Save(tag);

        tag.Add(new NbtTag<short>("BrewTime", (short)this.Stand.BrewTime));
        tag.Add(new NbtTag<byte>("Fuel", (byte)this.Stand.Fuel));
    }

    protected internal override ValueTask TickAsync() =>
        this.Level is AbstractLevel level ? level.TickBrewingStandAsync(this.Stand) : ValueTask.CompletedTask;
}
