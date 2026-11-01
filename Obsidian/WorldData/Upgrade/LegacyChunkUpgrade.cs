using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData.Structures;
using System.IO;

namespace Obsidian.WorldData.Upgrade;

/// <summary>
/// Rewrites the NBT of an Obsidian-written 1.21.11 world into 26.3's shape, one file's root compound at a time. The
/// world upgrade (<c>WorldUpgrade</c>) calls these for every chunk, entity chunk and structure template it moves.
/// </summary>
/// <remarks>
/// Each method returns the upgraded compound (it may be the same instance, changed in place) and leaves data that's
/// already in 26.3's shape alone, so running it twice is harmless. This is not a general vanilla data fixer: it only
/// covers what Obsidian wrote in 1.21.11.
/// <para>
/// Moving pistons (blocks mid-push) saved the block they move as a 1.21.11 state id, which nothing in 26.3 can
/// decode, so the upgrade drops them: the moving piston and the block it carried become air
/// (<c>docs/known-gaps/world-upgrade-drops-and-refusals.md</c>).
/// </para>
/// <para>
/// Obsidian builds before its vanilla-format saves (#539) saved chunk palettes by state id, in 1.21.11's numbering,
/// beside the block's name without its properties (see <see cref="HasPaletteIds"/>). Those states can't be decoded
/// without 1.21.11's state table, so such chunks are refused rather than upgraded with their properties reset:
/// <c>WorldUpgrade</c> checks the whole world before it changes anything.
/// </para>
/// </remarks>
internal static class LegacyChunkUpgrade
{
    /// <summary>
    /// A region chunk: block-state palettes, the chunk status, block entities and the <c>DataVersion</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">The chunk's palettes name states by id (see
    /// <see cref="HasPaletteIds"/>).</exception>
    public static NbtCompound UpgradeChunk(NbtCompound chunk)
    {
        if (HasPaletteIds(chunk))
        {
            throw new InvalidDataException("The chunk was saved by an Obsidian build before 26.3's vanilla-format "
                + "saves: its palettes name block states by 1.21.11 state id, which can't be upgraded.");
        }

        if (chunk.TryGetTag<NbtList>("sections", out var sections))
        {
            foreach (var section in sections.OfType<NbtCompound>())
            {
                if (section.TryGetTag<NbtCompound>("block_states", out var states))
                    UpgradePalette(states, "palette");
            }
        }

        if (chunk.TryGetTagValue<string>("Status", out var status))
        {
            var name = status.Replace("minecraft:", "", StringComparison.Ordinal);
            if (name is "noise" or "surface" or "carvers")
                Set(chunk, new NbtTag<string>("Status", "minecraft:terrain"));
        }
        var dropped = UpgradeBlockEntities(chunk, "block_entities");
        if (dropped.Count > 0)
            SetAir(chunk, dropped);

        UpgradeEntities(chunk, "entities");
        SetVersion(chunk);
        return chunk;
    }

    /// <summary>
    /// Whether a region chunk is in the format Obsidian saved before its vanilla-format saves (#539): a block-state
    /// palette entry carries a numeric <c>Id</c>. Only that writer put an <c>Id</c> in a palette entry, always beside a
    /// <c>Name</c> without the state's <c>Properties</c>; later builds save a <c>Name</c> and its <c>Properties</c>, like
    /// vanilla.
    /// </summary>
    public static bool HasPaletteIds(NbtCompound chunk)
    {
        if (!chunk.TryGetTag<NbtList>("sections", out var sections))
            return false;

        foreach (var section in sections.OfType<NbtCompound>())
        {
            if (section.TryGetTag<NbtCompound>("block_states", out var states)
                && states.TryGetTag<NbtList>("palette", out var palette)
                && palette.OfType<NbtCompound>().Any(entry => entry.HasTag("Id")))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// An entity chunk (<c>entities/r.x.z.mca</c>): block states held by entities and the <c>DataVersion</c>.
    /// </summary>
    public static NbtCompound UpgradeEntityChunk(NbtCompound chunk)
    {
        UpgradeEntities(chunk, "Entities");
        SetVersion(chunk);
        return chunk;
    }

    /// <summary>
    /// A structure template (<c>.nbt</c>): its block-state palettes.
    /// </summary>
    public static NbtCompound UpgradeStructureTemplate(NbtCompound template)
    {
        UpgradePalette(template, "palette");
        if (template.TryGetTag<NbtList>("palettes", out var palettes))
        {
            for (var i = 0; i < palettes.Count; i++)
            {
                if (palettes[i] is NbtList palette)
                    palettes[i] = UpgradePalette(palette);
            }
        }

        if (template.TryGetTag<NbtList>("blocks", out var blocks))
        {
            foreach (var block in blocks.OfType<NbtCompound>())
            {
                if (!block.TryGetTag<NbtCompound>("nbt", out var data))
                    continue;

                if (data.TryGetTagValue<string>("id", out var id) && id == "minecraft:bed")
                {
                    block.Remove("nbt");
                }
                else if (IsLegacyMovingPiston(data))
                {
                    block.Remove("nbt");
                    Set(block, new NbtTag<int>("state", TemplateAir(template)));
                }
                else
                {
                    UpgradeBlockEntity(data);
                }
            }
        }
        if (template.TryGetTag<NbtList>("entities", out var entities))
        {
            foreach (var entity in entities.OfType<NbtCompound>())
            {
                if (entity.TryGetTag<NbtCompound>("nbt", out var data))
                    UpgradeEntity(data);
            }
        }

        SetVersion(template);
        return template;
    }

    private static void UpgradePalette(NbtCompound parent, string key)
    {
        if (parent.TryGetTag<NbtList>(key, out var palette))
            Set(parent, UpgradePalette(palette));
    }

    private static NbtList UpgradePalette(NbtList palette)
    {
        var entries = palette.Select(UpgradeState).ToList();
        var stringsOnly = entries.All(entry => entry is NbtTag<string>);
        var upgraded = new NbtList(stringsOnly ? NbtTagType.String : NbtTagType.Compound, palette.Name!);
        foreach (var entry in entries)
            upgraded.Add(stringsOnly ? entry : BlockStateNbt.WrapPaletteEntry(entry));
        return upgraded;
    }

    private static INbtTag UpgradeState(INbtTag tag)
    {
        if (tag is not NbtCompound state)
            return tag;

        if (!state.TryGetTagValue<string>("Name", out var name))
            return tag;

        var properties = state.TryGetTag<NbtCompound>("Properties", out var values)
            ? values.Where(entry => entry.Value is NbtTag<string>)
                .ToDictionary(entry => entry.Key, entry => ((NbtTag<string>)entry.Value).Value!) : null;
        // Decide the compact form by the default's properties, without normalizing unknown/plugin state data.
        if (BlockStateProperties.TryGetState(name, null, out var defaultState)
            && (properties is null || properties.All(entry => defaultState.GetProperty(entry.Key) == entry.Value)))
        {
            return new NbtTag<string>(tag.Name!, name);
        }

        state.Remove("Name");
        state.Remove("Properties");
        Set(state, new NbtTag<string>("id", name));
        if (values is not null)
        {
            values.Name = "properties";
            Set(state, values);
        }
        return state;
    }

    private static void UpgradeStateField(NbtCompound parent, string key)
    {
        if (parent.TryGetTag(key, out var state))
            Set(parent, UpgradeState(state));
    }

    private static void UpgradeEntities(NbtCompound parent, string key)
    {
        if (parent.TryGetTag<NbtList>(key, out var entities))
        {
            foreach (var entity in entities.OfType<NbtCompound>())
                UpgradeEntity(entity);
        }
    }

    private static void UpgradeEntity(NbtCompound entity)
    {
        UpgradeStateField(entity, "carriedBlockState");
        UpgradeStateField(entity, "DisplayState");
        UpgradeEntities(entity, "Passengers");
    }

    // Upgrades a chunk's block entities, and gives the positions of the moving pistons dropped.
    private static List<(int X, int Y, int Z)> UpgradeBlockEntities(NbtCompound parent, string key)
    {
        var dropped = new List<(int X, int Y, int Z)>();
        if (!parent.TryGetTag<NbtList>(key, out var entities))
            return dropped;

        for (var i = entities.Count - 1; i >= 0; i--)
        {
            if (entities[i] is not NbtCompound entity)
                continue;

            if (entity.TryGetTagValue<string>("id", out var id) && id == "minecraft:bed")
            {
                entities.RemoveAt(i);
            }
            else if (IsLegacyMovingPiston(entity))
            {
                entities.RemoveAt(i);
                dropped.Add((entity.GetInt("x"), entity.GetInt("y"), entity.GetInt("z")));
            }
            else
            {
                UpgradeBlockEntity(entity);
            }
        }

        return dropped;
    }

    // A moving piston as 1.21.11 saved it: the block it moves as a 1.21.11 state id (see the remarks).
    private static bool IsLegacyMovingPiston(NbtCompound entity) =>
        entity.TryGetTagValue<string>("id", out var id)
        && id == "minecraft:piston"
        && entity.TryGetTag<NbtTag<int>>("MovedState", out _);

    // Sets the blocks at the positions to air, in the chunk's upgraded sections.
    private static void SetAir(NbtCompound chunk, List<(int X, int Y, int Z)> positions)
    {
        if (!chunk.TryGetTag<NbtList>("sections", out var sections))
            return;

        foreach (var section in sections.OfType<NbtCompound>())
        {
            if (!section.TryGetTag<NbtTag<byte>>("Y", out var sectionY)
                || !section.TryGetTag<NbtCompound>("block_states", out var states)
                || !states.TryGetTag<NbtList>("palette", out var palette)
                || palette.Count == 0)
            {
                continue;
            }

            var y = unchecked((sbyte)sectionY.Value);
            var indices = positions
                .Where(position => position.Y >> 4 == y)
                .Select(position => (position.Y & 15) << 8 | (position.Z & 15) << 4 | (position.X & 15))
                .ToList();

            if (indices.Count > 0)
                SetAir(states, palette, indices);
        }
    }

    // Sets entries of a section's block states to air, packing its data again for the palette's new size, with the
    // bits vanilla stores them with.
    private static void SetAir(NbtCompound states, NbtList palette, List<int> indices)
    {
        const int entries = 4096;

        var values = new int[entries];
        var oldBits = ChunkSectionNbt.BlockStorageBits(palette.Count);
        if (oldBits > 0 && states.TryGetTag<NbtArray<long>>("data", out var data))
        {
            var stored = new DataArray(oldBits, entries);
            if (data.GetArray().Length == stored.storage.Length)
            {
                stored.storage = data.GetArray();
                for (var i = 0; i < entries; i++)
                    values[i] = stored[i];
            }
        }

        var air = AirIndex(palette);
        foreach (var index in indices)
            values[index] = air;

        var bits = ChunkSectionNbt.BlockStorageBits(palette.Count);
        if (bits == 0)
        {
            states.Remove("data");
            return;
        }

        var packed = new DataArray(bits, entries);
        for (var i = 0; i < entries; i++)
            packed[i] = values[i];

        Set(states, new NbtArray<long>("data", packed.storage));
    }

    // The index of air in a template's palettes, added to each palette when it has none.
    private static int TemplateAir(NbtCompound template)
    {
        if (template.TryGetTag<NbtList>("palette", out var palette))
            return AirIndex(palette);

        if (!template.TryGetTag<NbtList>("palettes", out var palettes) || palettes.Count == 0)
            return 0;

        // The palettes are variants of one another, so a block's index has to be air in every one.
        var index = ((NbtList)palettes[0]).Count;
        foreach (var variant in palettes.OfType<NbtList>())
            AddAir(variant);

        return index;
    }

    // The index of air in an upgraded palette, added at its end when it has none.
    private static int AirIndex(NbtList palette)
    {
        for (var i = 0; i < palette.Count; i++)
        {
            // A mixed palette wraps its strings (see BlockStateNbt.WrapPaletteEntry).
            var entry = palette[i] is NbtCompound { Count: 1 } wrapper && wrapper.TryGetTag("", out var wrapped)
                ? wrapped
                : palette[i];

            if (BlockStateNbt.TryRead(entry, out var block) && block.RegistryId == BlocksRegistry.Air.RegistryId)
                return i;
        }

        AddAir(palette);
        return palette.Count - 1;
    }

    private static void AddAir(NbtList palette)
    {
        var air = BlockStateNbt.Write(BlocksRegistry.Air);
        palette.Add(palette.ListType == NbtTagType.String ? air : BlockStateNbt.WrapPaletteEntry(air));
    }

    private static void UpgradeBlockEntity(NbtCompound entity)
    {
        if (!entity.TryGetTagValue<string>("id", out var id))
            return;
        switch (id)
        {
            case "minecraft:furnace":
            case "minecraft:blast_furnace":
            case "minecraft:smoker":
                foreach (var key in new[] { "cooking_time_spent", "cooking_total_time", "lit_time_remaining", "lit_total_time" })
                    Widen(entity, key);
                break;
            case "minecraft:brewing_stand":
                Widen(entity, "BrewTime");
                Widen(entity, "Fuel");
                break;
            case "minecraft:piston":
                UpgradeStateField(entity, "blockState");
                break;
        }
        BlockEntityNbt.EnsureSaveDefaults(entity);
    }

    private static void Widen(NbtCompound compound, string key)
    {
        if (compound.TryGetTagValue<short>(key, out var small))
            Set(compound, new NbtTag<int>(key, small));
        else if (compound.TryGetTagValue<byte>(key, out var tiny))
            Set(compound, new NbtTag<int>(key, tiny));
    }

    private static void SetVersion(NbtCompound compound) => Set(compound, new NbtTag<int>("DataVersion", ServerConstants.DataVersion));

    private static void Set(NbtCompound compound, INbtTag tag)
    {
        compound.Remove(tag.Name!);
        compound.SetReadTag(tag.Name!, tag);
    }
}
