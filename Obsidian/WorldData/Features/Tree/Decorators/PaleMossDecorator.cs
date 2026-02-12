using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

[TreeProperty("minecraft:pale_moss")]
public sealed class PaleMossDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:pale_moss";

    public required double GroundProbability { get; init; } 
    public required double LeavesProbability { get; init; }
    public required double TrunkProbability { get; init; }

    public override ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions) => throw new NotImplementedException();
}
