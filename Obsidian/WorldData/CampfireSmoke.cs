using Obsidian.Entities.AI;

namespace Obsidian.WorldData;

/// <summary>The campfire smoke column used by hives and location predicates.</summary>
internal static class CampfireSmoke
{
    internal static bool Reaches(AbstractLevel level, Vector position)
    {
        lock (level.Fluids.TickLock)
        {
            // CampfireBlock.isSmokeyPos checks five cells against a central 4/16-wide post. A blocking
            // shape permits smoke only from the cell immediately below it, even at depth six.
            for (var depth = 1; depth <= 5; depth++)
            {
                var below = position + new Vector(0, -depth, 0);
                var block = level.GameEventBlock(below) ?? BlocksRegistry.Air;
                if (IsSource(block))
                    return true;

                var blocked = BlockCollisionShapes.Get(level, position, block).Any(box =>
                    box.Min.X < 0.625 && box.Max.X > 0.375
                    && box.Min.Z < 0.625 && box.Max.Z > 0.375
                    && box.Min.Y < 1 && box.Max.Y > 0);
                if (blocked)
                    return IsSource(level.GameEventBlock(below + Vector.Down));
            }

            return false;
        }
    }

    private static bool IsSource(IBlock? block) =>
        block is not null && block.GetProperty("lit") == "true"
        && TagsRegistry.Block.Campfires.Entries.Contains(block.RegistryId);
}
