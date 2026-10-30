using Obsidian.API.World.Features;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>The 26.3 speleothem shape pass used when templates place unshaped sulfur or dripstone columns.</summary>
internal static class SpeleothemShapes
{
    public static IBlock Update(IWorldGenLevel level, IBlock state, Vector position, BlockFace changedFace)
    {
        if (!changedFace.IsVertical())
        {
            return state;
        }

        var direction = FeatureHelpers.ParseFace(state.GetProperty("vertical_direction"));
        var behind = level.GetBlock(position.Offset(direction.Opposite()));
        var supported = behind.IsFaceSturdy(direction)
            || behind.RegistryId == state.RegistryId && HasDirection(behind, direction);

        // Unsupported speleothems retain their shape until their scheduled block tick. Feature regions have no
        // block tick executor, so this pass preserves the state rather than performing scheduled destruction.
        if (changedFace == direction.Opposite() && !supported)
        {
            return state;
        }

        var ahead = level.GetBlock(position.Offset(direction));
        var thickness = "tip";

        if (ahead.RegistryId == state.RegistryId && HasDirection(ahead, direction.Opposite()))
        {
            var merging = state.GetProperty("thickness") == "tip_merge"
                || ahead.GetProperty("thickness") == "tip_merge";
            thickness = merging ? "tip_merge" : "tip";
        }
        else if (HasDirection(ahead, direction))
        {
            if (ahead.GetProperty("thickness") is "tip" or "tip_merge")
            {
                thickness = "frustum";
            }
            else
            {
                thickness = HasDirection(behind, direction) ? "middle" : "base";
            }
        }

        return state.WithProperty("thickness", thickness);
    }

    private static bool HasDirection(IBlock state, BlockFace direction) =>
        state.BlockClass() is "PointedDripstoneBlock" or "SulfurSpikeBlock"
        && state.GetProperty("vertical_direction") == (direction == BlockFace.Up ? "up" : "down");
}
