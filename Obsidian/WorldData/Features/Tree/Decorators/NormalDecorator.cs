using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

[TreeProperty("minecraft:beehive")]
public sealed class NormalDecorator : DecoratorBase
{
    public override required string Type { get; init; }
}
