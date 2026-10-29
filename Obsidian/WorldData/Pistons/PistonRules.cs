using Obsidian.Registries;
using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Pistons;

/// <summary>
/// The rules vanilla's piston classes apply to block states: which states are piston bases, heads, or moving pieces, and
/// which blocks a piston can push (<c>PistonBaseBlock.isPushable</c>) and stick to (slime and honey).
/// </summary>
internal static class PistonRules
{
    public static bool IsBase(IBlock state) => state.Is(BlockIds.Piston) || state.Is(BlockIds.StickyPiston);

    public static bool IsHead(IBlock state) => state.Is(BlockIds.PistonHead);

    public static bool IsMoving(IBlock state) => state.Is(BlockIds.MovingPiston);

    /// <summary>Vanilla <c>PistonBaseBlock.isSticky</c>, dumped per state from the block's constructor flag.</summary>
    public static bool IsStickyPiston(IBlock state) => BlockPhysics.IsStickyPiston(state.BaseId);

    public static bool IsSticky(IBlock state) => state.GetProperty("type") == "sticky";

    public static bool IsExtended(IBlock state) => state.GetProperty("extended") == "true";

    public static BlockFace Facing(IBlock state) => Enum.Parse<BlockFace>(state.GetProperty("facing"), ignoreCase: true);

    /// <summary>Vanilla <c>isSticky</c> for a block that is pulled along: slime and honey.</summary>
    public static bool IsStickyBlock(IBlock state) => state.Is(BlockIds.SlimeBlock) || state.Is(BlockIds.HoneyBlock);

    /// <summary>Vanilla <c>PistonStructureResolver.canStickToEachOther</c>: honey and slime don't stick to each other.</summary>
    public static bool CanStickToEachOther(IBlock first, IBlock second)
    {
        if (first.Is(BlockIds.HoneyBlock) && second.Is(BlockIds.SlimeBlock))
            return false;

        if (first.Is(BlockIds.SlimeBlock) && second.Is(BlockIds.HoneyBlock))
            return false;

        return IsStickyBlock(first) || IsStickyBlock(second);
    }

    /// <summary>The push reaction name of the state (vanilla <c>getPistonPushReaction</c>, from the dumped physics).</summary>
    public static string PushReaction(IBlock state) => BlockPhysics.PistonPushReaction(state.BaseId);

    /// <summary>
    /// Vanilla <c>PistonBaseBlock.isPushable</c>: whether a piston moving toward <paramref name="direction"/> may move the
    /// block at <paramref name="position"/>. <paramref name="allowDestroy"/> is true for the blocks the planner would break,
    /// and <paramref name="pistonDirection"/> is the facing of the piston that pushes.
    /// </summary>
    public static bool IsPushable(IBlock state, IBlockChangeWorld world, Vector position, BlockFace direction, bool allowDestroy, BlockFace pistonDirection)
    {
        var top = world.MinY + world.Height - 1;
        if (position.Y < world.MinY || position.Y > top)
            return false;

        if (state.IsAir)
            return true;

        // Vanilla's unbreakable-by-piston list: obsidian, crying obsidian, respawn anchors, reinforced deepslate.
        if (state.Is(BlockIds.Obsidian) || state.Is(BlockIds.CryingObsidian) || state.Is(BlockIds.RespawnAnchor) || state.Is(BlockIds.ReinforcedDeepslate))
            return false;

        if (direction == BlockFace.Down && position.Y == world.MinY)
            return false;

        if (direction == BlockFace.Up && position.Y == top)
            return false;

        if (!IsBase(state))
        {
            if (BlockPhysics.MiningHardness(state.BaseId) == -1f)
                return false;

            switch (PushReaction(state))
            {
                case "IMMOVEABLE":
                    return false;
                case "POPPED":
                    return allowDestroy;
                case "PUSH":
                    return direction == pistonDirection;
            }
        }
        else if (IsExtended(state))
        {
            return false;
        }

        return !state.HasBlockEntity();
    }

    /// <summary>The axis of a face: 0 for x, 1 for y, 2 for z.</summary>
    public static int Axis(BlockFace face) => face switch
    {
        BlockFace.East or BlockFace.West => 0,
        BlockFace.Up or BlockFace.Down => 1,
        _ => 2,
    };

    /// <summary>The position <paramref name="steps"/> blocks away from <paramref name="position"/> in <paramref name="face"/>.</summary>
    public static Vector Step(Vector position, BlockFace face, int steps)
    {
        var delta = face.ToVector();
        return new Vector(position.X + delta.X * steps, position.Y + delta.Y * steps, position.Z + delta.Z * steps);
    }
}
