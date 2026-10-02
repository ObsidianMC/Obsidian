using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Runs <see cref="Delegate"/> on at most <see cref="Limit"/> random blocks of the template, like vanilla's
/// <c>CappedProcessor</c>. The blocks are picked from a random seeded by the world seed and the template's origin.
/// </summary>
[ConfiguredFeatureProperty("minecraft:capped")]
public sealed class CappedProcessor : StructureProcessor
{
    public required StructureProcessor Delegate { get; init; }

    public required IIntProvider Limit { get; init; }

    public override List<StructureBlockInfo> FinalizeProcessing(IWorldGenLevel level, Vector origin, Vector pivot,
        IReadOnlyList<StructureBlockInfo> originals, List<StructureBlockInfo> processed, StructurePlaceSettings settings)
    {
        if (this.Limit.MaxValue == 0 || processed.Count == 0 || originals.Count != processed.Count)
            return processed;

        var random = new LegacyRandomSource(level.Seed).ForkPositional().At(origin.X, origin.Y, origin.Z);
        var limit = Math.Min(this.Limit.Sample(random), processed.Count);
        if (limit < 1)
            return processed;

        // Vanilla Util.toShuffledList over the indices.
        var indices = Enumerable.Range(0, processed.Count).ToArray();
        for (var count = indices.Length; count > 1; count--)
        {
            var swap = random.NextInt(count);
            (indices[count - 1], indices[swap]) = (indices[swap], indices[count - 1]);
        }

        var changed = 0;
        for (var index = 0; index < indices.Length && changed < limit; index++)
        {
            var target = indices[index];
            var current = processed[target];
            var result = this.Delegate.ProcessBlock(level, origin, pivot, originals[target], current, settings);
            if (result is null || IsSame(current, result.Value))
                continue;

            changed++;
            processed[target] = result.Value;
        }

        return processed;
    }

    private static bool IsSame(StructureBlockInfo a, StructureBlockInfo b) =>
        a.Position == b.Position && a.Block.IsSameState(b.Block) && ReferenceEquals(a.Nbt, b.Nbt);
}
