using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>PathBlock</c> (the dirt path): its <c>updateShape</c> and <c>tick</c> overrides. A path under a solid
/// block other than a fence gate is scheduled to turn back into dirt a tick later.
/// </summary>
/// <remarks>Not ported: <c>Block.pushEntitiesUp</c> when it turns to dirt, as for farmland.</remarks>
internal sealed class PathBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    public IBlock? UpdateShape(
        IShapeLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor)
    {
        if (direction == BlockFace.Up && !CanSurvive(level, position))
            level.ScheduleTick(position, state, 1);

        return null;
    }

    /// <summary>Vanilla's <c>PathBlock.tick</c>: the path turns into dirt, whatever is above it by then.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var dirt = BlocksRegistry.Dirt;
        level.SetBlock(position, dirt);
        if (level.Level is AbstractLevel concrete)
            concrete.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)position + 0.5, affectedBlock: dirt);
    }

    // Vanilla's PathBlock.canSurvive: the block above isn't solid, or is a fence gate.
    private static bool CanSurvive(IShapeLevel level, Vector position)
    {
        var above = level.GetBlock(position + Vector.Up);
        return !above.IsSolid() || above.ClassId() == BlockClassId.FenceGateBlock;
    }
}
