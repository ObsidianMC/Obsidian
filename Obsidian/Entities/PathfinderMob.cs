namespace Obsidian.Entities;

public class PathfinderMob : Mob
{
    // Negative costs reject a node; positive costs make the pathfinder prefer safer terrain.
    protected internal virtual float GetPathCost(IBlock feet, IBlock floor)
    {
        if (feet.Material is Material.Lava or Material.Fire or Material.SoulFire or Material.PowderSnow ||
            floor.Material is Material.Lava or Material.MagmaBlock or Material.Cactus ||
            floor.UnlocalizedName.EndsWith("_fence", StringComparison.Ordinal) ||
            floor.UnlocalizedName.EndsWith("_wall", StringComparison.Ordinal) ||
            floor.UnlocalizedName.EndsWith("_fence_gate", StringComparison.Ordinal))
            return -1;
        return feet.Material == Material.Water ? 8 : 0;
    }
}
