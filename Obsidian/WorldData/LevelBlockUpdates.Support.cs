using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

internal sealed partial class LevelBlockUpdates
{
    // Vanilla BaseRailBlock.neighborChanged checks both the base and the raised end of an ascending rail.
    // This is dropResources/removeBlock, so it has no Level.destroyBlock break effect.
    private void RemoveUnsupportedRail(Vector position, IBlock state)
    {
        if (state.BlockClass() is not ("RailBlock" or "PoweredRailBlock" or "DetectorRailBlock"))
            return;

        var unsupported = !this.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up, SupportType.Rigid);
        if (state.GetProperty("shape") is string shape && shape.StartsWith("ascending_", StringComparison.Ordinal))
        {
            var raised = position.Offset(FeatureHelpers.ParseFace(shape[10..]));
            unsupported |= !this.GetBlock(raised).IsFaceSturdy(BlockFace.Up, SupportType.Rigid);
        }

        if (unsupported)
            this.DropAndRemove(position, state);
    }
}
