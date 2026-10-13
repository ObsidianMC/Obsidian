using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Nbt;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace Obsidian.WorldData;

/// <summary>
/// The components a block entity contributes to the items it drops, read from its saved data (vanilla's
/// <c>collectImplicitComponents</c>). A tag the block entity doesn't save copies nothing. A tag it does save but that
/// isn't modelled yet fails loudly rather than being left out of the drop.
/// </summary>
internal static class BlockEntityComponents
{
    /// <summary>
    /// The lookup for <c>copy_components</c>, or null when there's no block entity.
    /// </summary>
    public static Func<string, DataComponent?>? For(IBlockEntity? blockEntity)
    {
        if (blockEntity is null)
            return null;

        if (blockEntity is not DataBlockEntity data)
            throw new NotSupportedException($"Block entity {blockEntity.GetType().Name} has no component mapping yet.");

        return id => Get(data, id);
    }

    /// <summary>
    /// The stacks a container block entity spills when it's removed (vanilla <c>Containers.dropContents</c>, from
    /// <c>BlockEntity.preRemoveSideEffects</c>). Shulker boxes keep their contents for their drop instead, as vanilla overrides
    /// that method for them, so they spill nothing.
    /// </summary>
    public static IEnumerable<ItemStack> SpilledContents(IBlockEntity? blockEntity)
    {
        if (blockEntity is not DataBlockEntity data || data.Id.EndsWith("shulker_box", StringComparison.Ordinal))
            return [];

        return ParseContents(data.Data) ?? [];
    }

    /// <summary>
    /// The saved <c>Items</c> as slots: stacks are kept by slot up to the highest saved slot, and empty slots are air. Null when
    /// nothing is saved.
    /// </summary>
    private static ItemStack[]? ParseContents(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtList>("Items", out var items) || items.Count == 0)
            return null;

        var slots = new List<(int Slot, ItemStack Item)>();
        foreach (var itemTag in items.OfType<NbtCompound>())
        {
            if (!itemTag.TryGetTag<NbtTag<byte>>("Slot", out var slot))
                continue;
            slots.Add((slot.Value, itemTag.ItemFromNbt() ?? ItemStack.Air));
        }

        var contents = new ItemStack[slots.Count == 0 ? 0 : slots.Max(entry => entry.Slot) + 1];
        Array.Fill(contents, ItemStack.Air);
        foreach (var (slot, item) in slots)
            contents[slot] = item;

        return contents.Length == 0 ? null : contents;
    }

    private static DataComponent? Get(DataBlockEntity entity, string id)
    {
        var tag = entity.Data;
        switch (id)
        {
            case "minecraft:custom_name":
                return tag.TryGetTag("CustomName", out var name) && name.TextFromNbt() is { } chat
                    ? ComponentBuilder.CustomName with { Value = chat }
                    : null;

            case "minecraft:container":
                return ParseContents(tag) is { } parsed ? ComponentBuilder.Container with { Value = parsed } : null;

            case "minecraft:container_loot":
            {
                if (!tag.TryGetTag<NbtTag<string>>("LootTable", out var table))
                    return null;

                var seed = tag.TryGetTag<NbtTag<long>>("LootTableSeed", out var saved) ? saved.Value : 0L;
                return ComponentBuilder.ContainerLoot with
                {
                    Value = new NbtCompound { new NbtTag<string>("loot_table", table.Value), new NbtTag<long>("seed", seed) }
                };
            }

            case "minecraft:lock":
                // The saved lock is the item predicate a container opens with (vanilla's LockCode), kept as saved.
                if (!tag.TryGetTag<NbtCompound>("lock", out var lockTag))
                    return null;
                return ComponentBuilder.Lock with { Value = lockTag };

            case "minecraft:note_block_sound":
                return tag.TryGetTag<NbtTag<string>>("note_block_sound", out var sound)
                    ? ComponentBuilder.NoteBlockSound with { Value = sound.Value }
                    : null;

            case "minecraft:bees":
                return tag.TryGetTag<NbtList>("bees", out var bees) && bees.Count > 0
                    ? new BeesDataComponent { Occupants = bees.OfType<NbtCompound>().Select(Occupant).ToArray() }
                    : null;

            case "minecraft:pot_decorations":
                return tag.TryGetTag<NbtList>("sherds", out var sherds) && sherds.Count > 0
                    ? ComponentBuilder.PotDecorations with { Value = Sherds(sherds) }
                    : null;

            case "minecraft:profile":
                return tag.TryGetTag<NbtCompound>("profile", out var profile) ? Profile(profile) : null;

            case "minecraft:banner_patterns":
                return tag.TryGetTag<NbtList>("patterns", out var patterns) && patterns.Count > 0
                    ? new BannerPatternsDataComponent { Value = BannerLayers(patterns) }
                    : null;

            // Components no block entity supplies here.
            case "minecraft:item_name":
            case "minecraft:rarity":
            case "minecraft:tooltip_display":
                return null;

            default:
                throw new NotSupportedException($"copy_components of {id} isn't supported yet.");
        }
    }

    /// <summary>A saved bee: its entity data (with the type moved into the registry ID, as vanilla's TypedEntityData) and its ticks.</summary>
    private static (EntityDataComponent Entity, int TicksInHive, int MinTicksInHive) Occupant(NbtCompound saved)
    {
        if (!saved.TryGetTag<NbtCompound>("entity_data", out var entityData) || !entityData.TryGetTag<NbtTag<string>>("id", out var type)
            || !EntityNbt.TryParseType(type.Value, out var entityType))
            throw new NotSupportedException("A saved bee has no readable entity type.");

        var data = new NbtCompound();
        foreach (var (key, value) in entityData)
        {
            if (key != "id")
                data.Add(value);
        }

        var entity = new EntityDataComponent(DataComponentType.EntityData) { RegistryId = (int)entityType, Data = data };
        return (entity, saved.GetInt("ticks_in_hive"), saved.GetInt("min_ticks_in_hive"));
    }

    /// <summary>
    /// The saved banner layers (<c>pattern</c> and <c>color</c>), sent inline with the pattern's asset and translation key.
    /// </summary>
    private static BannerPatternLayer[] BannerLayers(NbtList patterns)
    {
        var layers = new List<BannerPatternLayer>();
        foreach (var saved in patterns.OfType<NbtCompound>())
        {
            var patternId = saved.GetString("pattern") ?? throw new NotSupportedException("A saved banner layer has no pattern.");
            var definition = BannerPatternDefinitions.Get(patternId)
                ?? throw new NotSupportedException($"Banner pattern {patternId} isn't defined by vanilla 1.21.11.");
            var color = saved.GetString("color") ?? throw new NotSupportedException($"Banner layer {patternId} has no color.");
            var dye = BannerPatternDefinitions.ParseColor(color)
                ?? throw new NotSupportedException($"Banner layer {patternId} has an unknown color {color}.");

            layers.Add(new BannerPatternLayer { AssetId = definition.AssetId, TranslationKey = definition.TranslationKey, Color = dye });
        }

        return layers.ToArray();
    }

    /// <summary>The four sides in vanilla's order (back, left, right, front). A missing or bare side is a brick.</summary>
    private static Item[] Sherds(NbtList sherds)
    {
        var items = new Item[4];
        for (var side = 0; side < items.Length; side++)
        {
            var name = side < sherds.Count && sherds[side] is NbtTag<string> sherd ? sherd.Value : "minecraft:brick";
            items[side] = ItemsRegistry.Get(name);
        }

        return items;
    }

    /// <summary>
    /// A saved skull profile. Only the name, id and properties are modelled, so any other saved key fails loudly.
    /// </summary>
    private static ProfileDataComponent Profile(NbtCompound saved)
    {
        foreach (var (key, _) in saved)
        {
            if (key is not ("name" or "id" or "properties"))
                throw new NotSupportedException($"profile key {key} isn't modelled yet.");
        }

        var profile = new ProfileDataComponent();
        if (saved.TryGetTag<NbtTag<string>>("name", out var name))
            profile.Username = name.Value;
        if (saved.TryGetTag<NbtArray<int>>("id", out var uuid) && uuid.Count == 4)
            profile.Id = FromIntArray(uuid);
        if (saved.TryGetTag<NbtList>("properties", out var properties))
        {
            profile.Properties = properties.OfType<NbtCompound>().Select(property => new SkinProperty
            {
                Name = property.GetString("name") ?? throw new NotSupportedException("A saved profile property has no name."),
                Value = property.GetString("value") ?? throw new NotSupportedException("A saved profile property has no value."),
                Signature = property.TryGetTag<NbtTag<string>>("signature", out var signature) ? signature.Value : null
            }).ToImmutableArray();
        }

        profile.IsComplete = profile.Id is not null && profile.Username is not null;
        return profile;
    }

    /// <summary>Vanilla's UUID NBT: four big-endian ints, most significant first.</summary>
    private static Guid FromIntArray(NbtArray<int> words)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (var i = 0; i < 4; i++)
            BinaryPrimitives.WriteInt32BigEndian(bytes[(i * 4)..], words[i]);
        return new Guid(bytes, bigEndian: true);
    }

    private static bool HasEntries(NbtCompound tag, string name) =>
        tag.TryGetTag<NbtList>(name, out var list) && list.Count > 0;
}
