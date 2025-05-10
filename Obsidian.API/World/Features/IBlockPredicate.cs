namespace Obsidian.API.World.Features;
public interface IBlockPredicate
{
    public string Type { get; }

    public bool GetResult(BlockPredicateContext context);
}

public readonly struct BlockPredicateContext
{
    public required IBlock Block { get; init; }

    public required Vector RelativeBlockPosition { get; init; }

    public required IWorld World { get; init; }

    public required IServer Server { get; init; }
}
