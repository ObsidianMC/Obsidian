using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

[TreeProperty("minecraft:cocoa")]
public sealed class CocoaDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:cocoa";

    public override ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions) => throw new NotImplementedException();
}
