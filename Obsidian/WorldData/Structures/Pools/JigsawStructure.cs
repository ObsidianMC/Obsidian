namespace Obsidian.WorldData.Structures.Pools;

/// <summary>
/// A structure assembled from template pools through jigsaw blocks (villages, outposts, bastions, ancient cities, trail
/// ruins, trial chambers), like vanilla's <c>JigsawStructure</c>.
/// </summary>
[StructureType("minecraft:jigsaw")]
public sealed class JigsawStructure : Structure
{
    /// <summary>The pool the start piece comes from.</summary>
    public required StructureTemplatePool StartPool { get; init; }

    /// <summary>When set, the start piece is placed so its jigsaw with this name is at the start position.</summary>
    public string? StartJigsawName { get; init; }

    /// <summary>How many pieces away from the start the structure may grow (0 to 20).</summary>
    public required int Size { get; init; }

    public required IHeightProvider StartHeight { get; init; }

    /// <summary>Vanilla's expansion hack: short pieces reserve room for the pieces that may grow on top of them.</summary>
    public required bool UseExpansionHack { get; init; }

    /// <summary>When set, the start height is relative to this heightmap at the start piece's center.</summary>
    public HeightmapType? ProjectStartToHeightmap { get; init; }

    /// <summary>
    /// How far pieces may reach from the start's center, horizontally and vertically (vanilla's single-number form).
    /// </summary>
    public required int MaxDistanceFromCenter { get; init; }

    public ImmutableArray<PoolAliasBinding> PoolAliases { get; init; } = [];

    /// <summary>
    /// Blocks pieces keep from the bottom and top of the world, or <c>null</c> when unset (vanilla's single-number form).
    /// </summary>
    public int? DimensionPadding { get; init; }

    public LiquidSettings LiquidSettings { get; init; } = LiquidSettings.ApplyWaterlogging;

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context)
    {
        var y = this.StartHeight.Sample(context.Random, context.Generation);
        var position = new Vector(context.ChunkX << 4, y, context.ChunkZ << 4);

        return JigsawPlacement.AddPieces(context, this.StartPool, this.StartJigsawName, this.Size, position, this.UseExpansionHack,
            this.ProjectStartToHeightmap, this.MaxDistanceFromCenter, PoolAliasLookup.Create(this.PoolAliases, position, context.Seed),
            this.DimensionPadding, this.LiquidSettings);
    }
}
