using Obsidian.API.World.Features.Trees;

namespace Obsidian.WorldData.ShapeRules;

/// <summary>
/// A vanilla class's <c>updateShape</c> that only the shared pass runs (<see cref="ShapeReach.SharedPass"/>): the
/// live engines reach it through <see cref="Features.Tree.ShapeUpdater"/> once no live owner answered, as generation
/// and placement's derivation do.
/// </summary>
internal abstract class SharedShapeRule : IShapeUpdateBehavior
{
    public ShapeReach Reach => ShapeReach.SharedPass;

    public abstract IBlock? UpdateShape(
        IShapeLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor);

    /// <summary>
    /// The level the shared pass reads, for the survival checks generation shares
    /// (<see cref="Features.BlockSurvival"/>). The shared pass only hands its rules views of a generation-shaped level,
    /// so this always succeeds there.
    /// </summary>
    protected static IWorldGenLevel Generation(IShapeLevel level) =>
        level as IWorldGenLevel
            ?? throw new InvalidOperationException("Shared shape rules run on the shared pass's views only.");

    protected static bool IsHorizontal(BlockFace face) => face is not (BlockFace.Up or BlockFace.Down);

    protected static bool SameAxis(BlockFace a, BlockFace b) => a == b || a == b.Opposite();
}
