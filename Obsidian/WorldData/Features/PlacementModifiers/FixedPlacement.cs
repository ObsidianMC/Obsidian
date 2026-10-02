using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Fixed positions; only those in the chunk being decorated are kept.
/// </summary>
[ConfiguredFeatureProperty("minecraft:fixed_placement")]
public sealed class FixedPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:fixed_placement";

    public required ImmutableArray<Vector> Positions { get; init; }

    public override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position)
    {
        var chunkX = position.X >> 4;
        var chunkZ = position.Z >> 4;
        return this.Positions.Where(fixedPosition => fixedPosition.X >> 4 == chunkX && fixedPosition.Z >> 4 == chunkZ);
    }
}
