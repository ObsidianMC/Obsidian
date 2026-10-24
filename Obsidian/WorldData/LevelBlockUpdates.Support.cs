using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

internal sealed partial class LevelBlockUpdates
{
    /// <summary>Rechecks vanilla updateShape survival for a queued shape destruction.</summary>
    internal bool IsUnsupported(Vector position, IBlock state) => updates.IsUnsupported(position, state);

    // Vanilla BaseRailBlock.neighborChanged checks both the base and the raised end of an ascending rail.
    // This is dropResources/removeBlock, so it has no Level.destroyBlock break effect.
    private void RemoveUnsupportedRail(Vector position, IBlock state)
    {
        if (!Rails.IsRail(state))
            return;

        if (this.IsUnsupportedRail(position, state))
            this.DropAndRemove(position, state);
    }

    /// <summary>
    /// Vanilla's <c>BaseRailBlock.shouldBeRemoved</c>: nothing rigid under the rail, or under a slope's raised end.
    /// </summary>
    internal bool IsUnsupportedRail(Vector position, IBlock state)
    {
        var unsupported = !this.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up, SupportType.Rigid);
        if (state.GetProperty("shape") is string shape && shape.StartsWith("ascending_", StringComparison.Ordinal))
        {
            var raised = position.Offset(FeatureHelpers.ParseFace(shape[10..]));
            unsupported |= !this.GetBlock(raised).IsFaceSturdy(BlockFace.Up, SupportType.Rigid);
        }

        return unsupported;
    }
}
