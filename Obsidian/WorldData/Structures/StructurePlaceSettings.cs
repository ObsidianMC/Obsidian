using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// How a <see cref="StructureTemplate"/> is placed, like vanilla's <c>StructurePlaceSettings</c> (without mirroring).
/// </summary>
/// <remarks>Mutable and per placement; don't share instances between threads.</remarks>
public sealed class StructurePlaceSettings
{
    public StructureRotation Rotation { get; set; }

    /// <summary>Template-relative point the rotation turns around.</summary>
    public Vector RotationPivot { get; set; } = Vector.Zero;

    /// <summary>Blocks outside this box aren't placed; <c>null</c> places everything.</summary>
    public BlockBox? BoundingBox { get; set; }

    /// <summary>
    /// Random used by processors and palette selection; when <c>null</c>, a random seeded from the position is used.
    /// </summary>
    public IRandomSource? Random { get; set; }

    public List<StructureProcessor> Processors { get; } = [];

    /// <summary>Vanilla <c>getRandom(pos)</c>.</summary>
    public IRandomSource GetRandom(Vector position) => this.Random ?? new LegacyRandomSource(Mth.GetSeed(position.X, position.Y, position.Z));

    /// <summary>Vanilla <c>getRandomPalette</c>: always draws <c>nextInt(count)</c>, even for a single palette.</summary>
    public IReadOnlyList<StructureBlockInfo> GetRandomPalette(IReadOnlyList<IReadOnlyList<StructureBlockInfo>> palettes, Vector position)
    {
        if (palettes.Count == 0)
            throw new InvalidOperationException("No palettes");

        return palettes[this.GetRandom(position).NextInt(palettes.Count)];
    }

    /// <summary>Replaces the processors with <paramref name="list"/>'s.</summary>
    public void SetProcessors(StructureProcessorList list)
    {
        this.Processors.Clear();
        this.Processors.AddRange(list.Processors);
    }
}
