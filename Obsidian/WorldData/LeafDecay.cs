using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>LeavesBlock</c>: leaves track how far they are from a log, and leaves that are too far from one (distance 7,
/// not persistent) decay on a random tick.
/// </summary>
/// <remarks>
/// Vanilla's <c>tick</c> recomputes the distance and <c>randomTick</c> decays, as listed in <c>eei</c> (<c>LeavesBlock</c>).
/// The scheduled tick also runs for any block update that reaches a leaf, not only the shape updates that vanilla schedules.
/// That recomputation gives the same distance, only earlier.
/// </remarks>
internal static class LeafDecay
{
    internal const int MaxDistance = 7;

    public static bool IsLeaves(IBlock block) => TreeBlocks.Leaves.Contains(block);

    // Vanilla getDistanceAt: 0 for logs, the distance of leaves, and 7 for anything else.
    public static int DistanceOf(IBlock block) => TreeBlocks.OptionalLeafDistance(block) ?? MaxDistance;

    // Vanilla decaying, which is also isRandomlyTicking.
    public static bool IsDecaying(IBlock block) =>
        IsLeaves(block) && DistanceOf(block) == MaxDistance && !IsPersistent(block);

    /// <summary>Vanilla's condition for <c>updateShape</c> to schedule the leaf's tick: the neighbor is a log, or the distance changes.</summary>
    public static bool ShouldScheduleTick(IBlock leaf, IBlock changedNeighbour)
    {
        var distance = DistanceOf(changedNeighbour) + 1;
        return distance != 1 || DistanceOf(leaf) != distance;
    }

    /// <summary>Vanilla <c>updateDistance</c>: the nearest log or leaf plus one, capped at 7.</summary>
    public static async ValueTask<IBlock> UpdateDistanceAsync(AbstractLevel level, Vector position, IBlock leaf)
    {
        var distance = MaxDistance;
        foreach (var direction in Vector.AllDirections)
        {
            var neighbour = await level.GetBlockAsync(position + direction) ?? BlocksRegistry.VoidAir;
            distance = Math.Min(distance, DistanceOf(neighbour) + 1);
        }

        return leaf.WithProperty("distance", Math.Min(distance, MaxDistance));
    }

    /// <summary>Vanilla <c>LeavesBlock.tick</c>: recomputes the distance of the leaf at <paramref name="position"/>.</summary>
    public static async ValueTask TickAsync(AbstractLevel level, Vector position)
    {
        var block = await level.GetBlockAsync(position);
        if (block is null || !IsLeaves(block))
            return;

        var updated = await UpdateDistanceAsync(level, position, block);
        if (!updated.IsSameState(block))
            await level.SetBlockAsync(position, updated, true);
    }

    /// <summary>
    /// Vanilla <c>LeavesBlock.randomTick</c>: a decaying leaf drops its loot (<c>dropResources</c>, with no tool) and is replaced
    /// by the fluid there. The drops don't check doTileDrops, as vanilla's <c>getDrops</c> doesn't.
    /// </summary>
    public static async ValueTask RandomTickAsync(AbstractLevel level, Vector position)
    {
        var block = await level.GetBlockAsync(position);
        if (block is null || !IsDecaying(block))
            return;

        var drops = await BlockDrops.ForDestroyedAsync(level, block, position, null);
        await level.SetBlockAsync(position, FluidState.Of(block).CreateLegacyBlock(), true);
        foreach (var stack in drops)
            BlockDrops.SpawnDrop(level, () => Server.GetNextEntityId(), position, stack);
    }

    private static bool IsPersistent(IBlock block) => bool.TryParse(block.GetProperty("persistent"), out var persistent) && persistent;
}
