using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Offsets the position by random amounts (X, then Y, then Z are sampled).
/// </summary>
[ConfiguredFeatureProperty("minecraft:random_offset")]
public sealed class RandomOffsetPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:random_offset";

    public required IIntProvider XzSpread { get; init; }

    public required IIntProvider YSpread { get; init; }

    public override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position)
    {
        var x = position.X + this.XzSpread.Sample(random);
        var y = position.Y + this.YSpread.Sample(random);
        var z = position.Z + this.XzSpread.Sample(random);
        return [new Vector(x, y, z)];
    }
}
