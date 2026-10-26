namespace Obsidian.WorldData.Placement;

/// <summary>
/// Vanilla's placement rule for waterloggable blocks: a block placed where there is water takes that water, so it is placed with
/// <c>waterlogged=true</c>. Flowing water counts too: it is the same fluid type, and the fluid system stores its water the same way.
/// </summary>
internal static class Waterlogging
{
    public static IBlock Derive(IBlock block, IBlock current)
    {
        if (block.GetProperty("waterlogged") is null)
            return block;

        return current.Is(BlockIds.Water) ? block.WithProperty("waterlogged", true) : block;
    }
}
