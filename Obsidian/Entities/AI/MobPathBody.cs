using Obsidian.API.AI;
using Obsidian.API.World;

namespace Obsidian.Entities.AI;

/// <summary>The path body of a mob: forwards each member to the mob and its navigator.</summary>
internal sealed class MobPathBody(PathfinderMob mob) : IPathBody
{
    public VectorD Position => mob.Position;

    public EntityDimension Dimension => mob.Dimension;

    public ILevel Level => mob.Level;

    public IPathTerrain Terrain => mob.Terrain;

    public float StepHeight => mob.GetAttributeValue("minecraft:generic.step_height");

    // Vanilla's cW(): without an attack target, 3; with one, the health-based limit from Mob.cW().
    public int MaxFall
    {
        get
        {
            if (mob.AttackTarget == null)
                return 3;
            var value = (int)(mob.Health - mob.GetAttributeValue("minecraft:generic.max_health") * 0.33f);
            value -= (3 - (int)mob.Level.LevelData.Difficulty) * 4;
            return (int)Math.Ceiling(Math.Max(0, value) + 3f);
        }
    }

    public float FollowRange => mob.FollowRange;

    public bool CanOpenDoors => mob.Navigator is Navigator { CanOpenDoors: true };

    public bool CanFloat => mob.Navigator is Navigator { CanFloat: true };

    public bool SwimmingNavigation => mob.SwimmingNavigation;

    public bool FlyingNavigation => mob.FlyingNavigation;

    public bool InWater => mob.InWater;

    public float Malus(PathNodeType type) => mob.GetPathfindingMalus(PathNodeMapping.ToInternal(type));

    public float NodeCost(Vector feetPosition, IBlock feet, IBlock floor) => mob.GetPathCost(feetPosition, feet, floor);
}
