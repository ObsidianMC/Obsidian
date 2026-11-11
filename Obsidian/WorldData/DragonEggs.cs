using Obsidian.API.Blocks;
using Obsidian.Entities;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>DragonEggBlock</c>: used or hit by a survival player, the egg jumps to a random free spot on a block
/// within 15 blocks across and 7 up or down, as long as it finds one in a thousand tries.
/// </summary>
internal static class DragonEggs
{
    private const int Tries = 1000;

    private const int HorizontalRadius = 16;

    private const int VerticalRadius = 8;

    // Vanilla's LevelEvent.PARTICLES_DRAGON_EGG_TELEPORT: the trail of particles from the egg to where it went.
    private const int TeleportEvent = 2015;

    /// <summary>Teleports the dragon egg at <paramref name="position"/>, if there is one.</summary>
    internal static void Teleport(AbstractLevel level, Vector position)
    {
        // The tick reads and draws from the level's random too, so the search runs under its lock.
        lock (level.Fluids.TickLock)
        {
            var egg = level.BlockUpdates.GetBlock(position);
            if (!egg.Is(BlockIds.DragonEgg))
                return;

            if (FindDestination(level, position) is not Vector destination)
                return;

            level.BroadcastLevelEvent(TeleportEvent, position, PackOffset(position, destination));
            level.BlockUpdates.SetBlock(destination, egg, BlockUpdateFlags.Clients);
            level.BlockUpdates.RemoveBlock(position);
        }
    }

    // Vanilla's search, which clients and seeds depend on: per try, two draws for x, then two for y, then two for z
    // from the level's random, each the difference of two draws below the radius. The spot must be air, above a block
    // that isn't, inside the world border and the build height.
    private static Vector? FindDestination(AbstractLevel level, Vector position)
    {
        var random = level.GrowthRandom;
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var dx = random.NextInt(HorizontalRadius) - random.NextInt(HorizontalRadius);
            var dy = random.NextInt(VerticalRadius) - random.NextInt(VerticalRadius);
            var dz = random.NextInt(HorizontalRadius) - random.NextInt(HorizontalRadius);
            var candidate = position + new Vector(dx, dy, dz);

            if (IsFree(level, candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsFree(AbstractLevel level, Vector candidate)
    {
        if (candidate.Y < level.MinY || candidate.Y >= level.MinY + level.Height)
            return false;

        if (!level.BlockUpdates.GetBlock(candidate).IsAir)
            return false;

        if (level.BlockUpdates.GetBlock(candidate + Vector.Down).IsAir)
            return false;

        return InsideBorder(level, candidate);
    }

    // Vanilla's WorldBorder.isWithinBounds for a block: its corner inside the border's square.
    private static bool InsideBorder(AbstractLevel level, Vector candidate)
    {
        var data = level.LevelData;
        var half = data.BorderSize / 2;
        return candidate.X >= data.BorderCenterX - half
            && candidate.X < data.BorderCenterX + half
            && candidate.Z >= data.BorderCenterZ - half
            && candidate.Z < data.BorderCenterZ + half;
    }

    // Vanilla's BlockUtil.packDifferenceInPosition with the egg's radii: each offset shifted by its radius into a byte,
    // x in the third byte, y in the second, z in the first. Clients unpack it to draw the trail.
    internal static int PackOffset(Vector from, Vector to)
    {
        var x = (to.X - from.X + HorizontalRadius) & 0xFF;
        var y = (to.Y - from.Y + VerticalRadius) & 0xFF;
        var z = (to.Z - from.Z + HorizontalRadius) & 0xFF;
        return x << 16 | y << 8 | z;
    }
}

internal static partial class UseBehaviors
{
    /// <summary>Vanilla's <c>DragonEggBlock</c>: a use teleports the egg, whatever the player holds.</summary>
    internal sealed class DragonEggUse : IBlockUseBehavior, IBlockAttackBehavior
    {
        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            DragonEggs.Teleport(use.Level, use.Position);
            return ValueTask.FromResult(UseResult.Success);
        }

        public ValueTask AttackAsync(AbstractLevel level, Vector position, IBlock block, PlayerBase player)
        {
            DragonEggs.Teleport(level, position);
            return ValueTask.CompletedTask;
        }
    }
}
