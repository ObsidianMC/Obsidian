namespace Obsidian.WorldData.ShapeRules;

/// <summary>
/// A vanilla class's <c>updateShape</c> that only the shared pass runs (<see cref="ShapeReach.SharedPass"/>): a live
/// level's shape update reaches it through <see cref="Features.Tree.ShapeUpdater"/> once no live owner answered, as
/// generation and placement's derivation do.
/// </summary>
internal abstract class SharedShapeRule : IShapeUpdateBehavior
{
    public ShapeReach Reach => ShapeReach.SharedPass;

    public IShapeUpdateBehavior? SharedPassRule => null;

    public abstract IBlock? UpdateShape(
        IShapeLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor);

    protected static bool IsHorizontal(BlockFace face) => face is not (BlockFace.Up or BlockFace.Down);

    protected static bool SameAxis(BlockFace a, BlockFace b) => a == b || a == b.Opposite();
}
