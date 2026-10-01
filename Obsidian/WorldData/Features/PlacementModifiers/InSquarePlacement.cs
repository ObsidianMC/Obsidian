using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Moves the position to a random column of its 16x16 area.
/// </summary>
[ConfiguredFeatureProperty("minecraft:in_square")]
public sealed class InSquarePlacement : PlacementModifierBase
{
    public override string Type => "minecraft:in_square";

    public override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position)
    {
        var x = random.NextInt(16) + position.X;
        var z = random.NextInt(16) + position.Z;
        return [new Vector(x, position.Y, z)];
    }
}
