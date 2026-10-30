using Obsidian.Nbt;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>
/// Defaults of 26.3 decoration's data-backed sculk block entities. Vanilla creates these from pending protochunk
/// tags when read; materialize their initial saved data at the features boundary, before chunks can be persisted.
/// </summary>
internal static class DecorationBlockEntities
{
    public static void Initialize(IEnumerable<IChunk> chunks)
    {
        foreach (var chunk in chunks.OfType<Chunk>())
        {
            foreach (var entity in chunk.BlockEntities.Values.OfType<DataBlockEntity>())
            {
                switch (entity.Id)
                {
                    case "minecraft:sculk_catalyst":
                        Add(entity, new NbtList(NbtTagType.Compound, "cursors"));
                        break;
                    case "minecraft:sculk_sensor":
                    case "minecraft:calibrated_sculk_sensor":
                        Add(entity, new NbtTag<int>("last_vibration_frequency", 0));
                        Add(entity, Listener());
                        break;
                    case "minecraft:sculk_shrieker":
                        Add(entity, new NbtTag<int>("warning_level", 0));
                        Add(entity, Listener());
                        break;
                }
            }
        }
    }

    private static NbtCompound Listener() => new("listener")
    {
        new NbtCompound("selector") { new NbtTag<long>("tick", -1L) },
        new NbtTag<int>("event_delay", 0)
    };

    private static void Add(DataBlockEntity entity, INbtTag field)
    {
        if (!entity.Data.HasTag(field.Name!))
        {
            entity.Set(field);
        }
    }
}
