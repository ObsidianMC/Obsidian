using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// How a <see cref="StructureTemplate"/> is placed, like vanilla's <c>StructurePlaceSettings</c>.
/// </summary>
/// <remarks>Mutable and per placement; don't share instances between threads.</remarks>
public sealed class StructurePlaceSettings
{
    public StructureMirror Mirror { get; set; }

    public StructureRotation Rotation { get; set; }

    /// <summary>Template-relative point the rotation turns around.</summary>
    public Vector RotationPivot { get; set; } = Vector.Zero;

    /// <summary>Blocks and entities outside this box aren't placed; <c>null</c> places everything.</summary>
    public BlockBox? BoundingBox { get; set; }

    /// <summary>
    /// Random used by processors and palette selection; when <c>null</c>, a random seeded from the position is used.
    /// </summary>
    public IRandomSource? Random { get; set; }

    /// <summary>Whether the template's entities are left out.</summary>
    public bool IgnoreEntities { get; set; }

    /// <summary>
    /// Whether the blocks already have their final shape: placement then skips the neighbor shape updates (jigsaw pieces).
    /// </summary>
    public bool KnownShape { get; set; }

    /// <summary>Whether placed mobs get vanilla's structure spawn finalization.</summary>
    public bool FinalizeEntities { get; set; }

    public LiquidSettings LiquidSettings { get; set; } = LiquidSettings.ApplyWaterlogging;

    public List<StructureProcessor> Processors { get; } = [];

    /// <summary>Vanilla <c>shouldApplyWaterlogging</c>: waterloggable blocks placed in water get waterlogged.</summary>
    public bool ShouldApplyWaterlogging => this.LiquidSettings == LiquidSettings.ApplyWaterlogging;

    /// <summary>Vanilla <c>getRandom(pos)</c>.</summary>
    public IRandomSource GetRandom(Vector position) => this.Random ?? new LegacyRandomSource(Mth.GetSeed(position.X, position.Y, position.Z));

    /// <summary>Vanilla <c>getRandomPalette</c>: always draws <c>nextInt(count)</c>, even for a single palette.</summary>
    public T GetRandomPalette<T>(IReadOnlyList<T> palettes, Vector position)
    {
        if (palettes.Count == 0)
            throw new InvalidOperationException("No palettes");

        return palettes[this.GetRandom(position).NextInt(palettes.Count)];
    }

    /// <summary>Vanilla <c>copy</c>: the same settings, with their own processor list.</summary>
    public StructurePlaceSettings Copy()
    {
        var copy = new StructurePlaceSettings
        {
            Mirror = this.Mirror,
            Rotation = this.Rotation,
            RotationPivot = this.RotationPivot,
            BoundingBox = this.BoundingBox,
            Random = this.Random,
            IgnoreEntities = this.IgnoreEntities,
            KnownShape = this.KnownShape,
            FinalizeEntities = this.FinalizeEntities,
            LiquidSettings = this.LiquidSettings
        };

        copy.Processors.AddRange(this.Processors);
        return copy;
    }

    /// <summary>Replaces the processors with <paramref name="list"/>'s.</summary>
    public void SetProcessors(StructureProcessorList list)
    {
        this.Processors.Clear();
        this.Processors.AddRange(list.Processors);
    }
}

/// <summary>
/// Whether templates waterlog their blocks placed in water, like vanilla's <c>LiquidSettings</c>.
/// </summary>
public enum LiquidSettings
{
    IgnoreWaterlogging,
    ApplyWaterlogging
}
