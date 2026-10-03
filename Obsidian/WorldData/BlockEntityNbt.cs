using Obsidian.API.Containers;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

/// <summary>
/// Saves and loads a chunk's block entities in vanilla's form (the entries of a chunk's <c>block_entities</c>).
/// </summary>
internal static class BlockEntityNbt
{
    /// <summary>
    /// Saves <paramref name="blockEntity"/> of <paramref name="chunk"/> with its <c>id</c> and position, or returns
    /// <c>null</c> for block entities that aren't saved yet.
    /// </summary>
    /// <remarks>
    /// Containers are saved like vanilla's <c>BaseContainerBlockEntity</c>: <c>CustomName</c> and their <c>Items</c>, each
    /// with its <c>Slot</c>. A container is only made once it's opened, so the loot table it was waiting for is gone by
    /// then; containers that weren't opened are data block entities that keep their <c>LootTable</c>.
    /// </remarks>
    public static NbtCompound? Save(IBlockEntity blockEntity, IChunk chunk)
    {
        var position = blockEntity.BlockPosition;
        switch (blockEntity)
        {
            case DataBlockEntity data:
            {
                var tag = Header(data.Id, position);
                foreach (var (name, child) in data.Data)
                    tag.Add(name, child);

                return tag;
            }
            case BaseContainer container:
            {
                // Container ids are Obsidian's own, so the id comes from the block; a container whose block is gone isn't saved.
                var id = chunk.GetBlock(position.X, position.Y, position.Z).BlockEntityType();
                if (id is null)
                    return null;

                var tag = Header(id, position);
                if (container.CustomName is not null)
                    tag.Add(container.CustomName.ToNbt("CustomName"));

                var items = new NbtList(NbtTagType.Compound, "Items");
                for (var slot = 0; slot < container.Size; slot++)
                {
                    var item = container[slot];
                    if (item is null || item.IsAir || item.Count <= 0)
                        continue;

                    var itemTag = item.ToNbt();
                    itemTag.Add(new NbtTag<byte>("Slot", (byte)slot));
                    items.Add(itemTag);
                }

                tag.Add(items);
                return tag;
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Loads a saved block entity: a container when it has <c>Items</c> and Obsidian has a container for its type, or else
    /// a <see cref="DataBlockEntity"/> keeping the saved fields.
    /// </summary>
    public static IBlockEntity Load(NbtCompound tag, string id, Vector position)
    {
        if (tag.HasTag("Items") && !tag.HasTag("LootTable") && CreateContainer(id, position) is BaseContainer container)
        {
            LoadContents(tag, container);
            return (IBlockEntity)container;
        }

        var data = new NbtCompound();
        foreach (var (name, child) in tag)
        {
            if (name is not ("id" or "x" or "y" or "z" or "keepPacked"))
                data.Add(name, child);
        }

        return new DataBlockEntity { Id = id, BlockPosition = position, Data = data };
    }

    /// <summary>
    /// Fills <paramref name="container"/> with the <c>Items</c> and <c>CustomName</c> saved in <paramref name="tag"/>,
    /// like vanilla's <c>BaseContainerBlockEntity.loadAdditional</c>. Slots past the container's size are skipped.
    /// </summary>
    public static void LoadContents(NbtCompound tag, BaseContainer container)
    {
        if (tag.TryGetTag<NbtList>("Items", out var items))
        {
            foreach (var itemTag in items.OfType<NbtCompound>())
            {
                if (!itemTag.TryGetTag<NbtTag<byte>>("Slot", out var slot) || slot.Value >= container.Size)
                    continue;

                container.SetItem(slot.Value, itemTag.ItemFromNbt());
            }
        }

        if (tag.TryGetTag("CustomName", out var customName) && customName.TextFromNbt() is ChatMessage name)
        {
            container.CustomName = name;
            container.Title = name;
        }
    }

    private static NbtCompound Header(string id, Vector position) => new()
    {
        new NbtTag<string>("id", id),
        new NbtTag<int>("x", position.X),
        new NbtTag<int>("y", position.Y),
        new NbtTag<int>("z", position.Z),
        new NbtTag<bool>("keepPacked", false)
    };

    // The containers MainEventHandler opens for these blocks, with the same titles. Hoppers and the rest stay data block
    // entities.
    private static BaseContainer? CreateContainer(string id, Vector position) => id switch
    {
        "minecraft:chest" or "minecraft:trapped_chest" => new Container { Id = "chest", Title = "Chest", BlockPosition = position },
        "minecraft:barrel" => new Container { Id = "barrel", Title = "Barrel", BlockPosition = position },
        "minecraft:shulker_box" => new Container { Id = "shulker_box", Title = "Shulker Box", BlockPosition = position },
        "minecraft:dispenser" => new Container(9) { Id = "dispenser", Title = "Dispenser", BlockPosition = position },
        "minecraft:dropper" => new Container(9) { Id = "dropper", Title = "Dropper", BlockPosition = position },
        "minecraft:furnace" => new SmeltingContainer(InventoryType.Furnace, "furnace") { Title = "Furnace", BlockPosition = position },
        "minecraft:blast_furnace" => new SmeltingContainer(InventoryType.BlastFurnace, "blast_furnace") { Title = "BlastFurnace", BlockPosition = position },
        "minecraft:smoker" => new SmeltingContainer(InventoryType.Smoker, "smoker") { Title = "Smoker", BlockPosition = position },
        "minecraft:brewing_stand" => new BrewingStand { BlockPosition = position },
        _ => null
    };
}
