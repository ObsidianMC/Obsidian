using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

[TreeProperty("minecraft:creaking_heart")]
public sealed class CreakingHeartDecorator : DecoratorBase
{
    public override string Type { get; init;  } = "minecraft:creaking_heart";

    public override ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions) => throw new NotImplementedException();
}
