using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

[TreeProperty("minecraft:leave_vine")]
public sealed class LeaveVineDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:leave_vine";

    public override ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions) => throw new NotImplementedException();
}
