namespace Obsidian.Entities;

public class PathfinderMob : Mob
{
    // Negative costs reject a node; positive costs make the pathfinder prefer safer terrain.
    protected internal virtual float GetPathCost(IBlock feet, IBlock floor)
    {
        if (feet.RegistryId is BlockIds.Lava or BlockIds.Fire or BlockIds.SoulFire or BlockIds.PowderSnow ||
            floor.RegistryId is BlockIds.Lava or BlockIds.MagmaBlock or BlockIds.Cactus ||
            floor.UnlocalizedName.EndsWith("_fence", StringComparison.Ordinal) ||
            floor.UnlocalizedName.EndsWith("_wall", StringComparison.Ordinal) ||
            floor.UnlocalizedName.EndsWith("_fence_gate", StringComparison.Ordinal))
            return -1;
        return feet.RegistryId == BlockIds.Water ? 8 : 0;
    }
}
