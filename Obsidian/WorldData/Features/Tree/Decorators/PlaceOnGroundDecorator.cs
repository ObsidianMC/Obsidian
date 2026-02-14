using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

[TreeProperty("minecraft:place_on_ground")]
public sealed class PlaceOnGroundDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:place_on_ground";

    public required int Height { get; init; }
    public required int Radius { get; init; }
    public required int Tries { get; init; }

    public required IBlockStateProvider BlockStateProvider { get; init; }

    public override ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions) => throw new NotImplementedException();
}
