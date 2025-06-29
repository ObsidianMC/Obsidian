using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

[TreeProperty("minecraft:alter_ground")]
public sealed class AlterGroundDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:alter_ground";

    public required IBlockStateProvider Provider { get; set; }
}
