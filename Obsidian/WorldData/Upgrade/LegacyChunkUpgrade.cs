using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData.Structures;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Upgrade;

/// <summary>
/// Rewrites the NBT of an Obsidian-written 1.21.11 world into 26.3's shape, one file's root compound at a time. The
/// world upgrade (<c>WorldUpgrade</c>) calls these for every chunk, entity chunk and structure template it moves.
/// </summary>
/// <remarks>
/// Each method returns the upgraded compound (it may be the same instance, changed in place) and leaves data that's
/// already in 26.3's shape alone, so running it twice is harmless. This is not a general vanilla data fixer: it only
/// covers what Obsidian wrote in 1.21.11.
/// </remarks>
internal static class LegacyChunkUpgrade
{
    /// <summary>
    /// A region chunk: block-state palettes, the chunk status, block entities and the <c>DataVersion</c>.
    /// </summary>
    public static NbtCompound UpgradeChunk(NbtCompound chunk)
    {
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
        UpgradeBlockEntities(chunk, "block_entities");
        UpgradeEntities(chunk, "entities");
        SetVersion(chunk);
        return chunk;
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
                if (block.TryGetTag<NbtCompound>("nbt", out var data))
                {
                    if (data.TryGetTagValue<string>("id", out var id) && id == "minecraft:bed")
                        block.Remove("nbt");
                    else
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

        // Obsidian's earlier palettes saved the state id, in 1.21.11's numbering, often beside a Name without the
        // state's Properties. 1.21.11's reader went by the id, so it wins over the Name; one the table can't decode
        // falls back to the Name and Properties.
        if (state.TryGetTag<NbtTag<int>>("Id", out var stateId))
        {
            state.Remove("Id");
            if (LegacyState(stateId.Value) is IBlock legacy)
                return BlockStateNbt.Write(legacy, tag.Name!);
        }

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

    private static void UpgradeBlockEntities(NbtCompound parent, string key)
    {
        if (!parent.TryGetTag<NbtList>(key, out var entities))
            return;
        for (var i = entities.Count - 1; i >= 0; i--)
        {
            if (entities[i] is not NbtCompound entity)
                continue;
            if (entity.TryGetTagValue<string>("id", out var id) && id == "minecraft:bed")
                entities.RemoveAt(i);
            else
                UpgradeBlockEntity(entity);
        }
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
                if (entity.TryGetTagValue<int>("MovedState", out var state))
                {
                    if (LegacyState(state) is not IBlock block)
                        throw new InvalidDataException($"Cannot upgrade legacy moving-piston state {state}.");
                    entity.Remove("MovedState");
                    Set(entity, BlockStateNbt.Write(block, "blockState"));
                    var face = entity.TryGetTagValue<string>("Facing", out var facing) && Enum.TryParse<BlockFace>(facing, true, out var parsed)
                        ? parsed : BlockFace.North;
                    entity.Remove("Facing");
                    Set(entity, new NbtTag<int>("facing", (int)face));
                    Rename(entity, "Extending", "extending");
                    Rename(entity, "Source", "source");
                    Rename(entity, "Progress", "progress");
                }
                UpgradeStateField(entity, "blockState");
                break;
        }
        BlockEntityNbt.EnsureSaveDefaults(entity);
    }

    // A state id in 1.21.11's numbering, or null for one that isn't in its table or whose state 26.3 doesn't have.
    private static IBlock? LegacyState(int id)
    {
        var legacy = legacyStates.Value;
        return id >= 0 && id < legacy.Length ? BlockStateParser.TryParse(legacy[id]) : null;
    }

    private static readonly Lazy<string[]> legacyStates = new(() =>
    {
        using var stream = typeof(LegacyChunkUpgrade).Assembly.GetManifestResourceStream("Obsidian.Assets.legacy_block_states.json")
            ?? throw new InvalidOperationException("Missing legacy block-state table.");
        return JsonSerializer.Deserialize<string[]>(stream)!;
    });

    private static void Widen(NbtCompound compound, string key)
    {
        if (compound.TryGetTagValue<short>(key, out var small))
            Set(compound, new NbtTag<int>(key, small));
        else if (compound.TryGetTagValue<byte>(key, out var tiny))
            Set(compound, new NbtTag<int>(key, tiny));
    }

    private static void Rename(NbtCompound compound, string oldName, string name)
    {
        if (!compound.TryGetTag(oldName, out var tag))
            return;
        compound.Remove(oldName);
        tag.Name = name;
        Set(compound, tag);
    }

    private static void SetVersion(NbtCompound compound) => Set(compound, new NbtTag<int>("DataVersion", ServerConstants.DataVersion));

    private static void Set(NbtCompound compound, INbtTag tag)
    {
        compound.Remove(tag.Name!);
        compound.SetReadTag(tag.Name!, tag);
    }
}
