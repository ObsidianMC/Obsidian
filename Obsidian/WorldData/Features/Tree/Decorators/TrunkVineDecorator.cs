using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

[TreeProperty("minecraft:trunk_vine")]
public sealed class TrunkVineDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:trunk_vine";

    public override ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions) => throw new NotImplementedException();
}
