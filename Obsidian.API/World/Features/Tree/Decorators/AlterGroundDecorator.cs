namespace Obsidian.API.World.Features.Tree.Decorators;
public sealed class AlterGroundDecorator : DecoratorBase
{
    public override string Type => "alter_ground";

    public required IBlockStateProvider Provider { get; set; }
}
