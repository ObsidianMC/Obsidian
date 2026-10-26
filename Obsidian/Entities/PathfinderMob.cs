using Obsidian.Entities.AI;

namespace Obsidian.Entities;

public class PathfinderMob : Mob
{
    // Vanilla Mob's per-type malus, starting from the generated PathType defaults; species constructors override entries.
    private readonly float[] pathfindingMalus = CreateDefaultMalus();

    internal float GetPathfindingMalus(PathType type) => this.pathfindingMalus[(int)type];

    internal void SetPathfindingMalus(PathType type, float malus) => this.pathfindingMalus[(int)type] = malus;

    private static float[] CreateDefaultMalus()
    {
        var types = Enum.GetValues<PathType>();
        var malus = new float[types.Length];
        foreach (var type in types)
            malus[(int)type] = PathTypeMalus.Get(type);
        return malus;
    }

    // Negative costs reject a node; positive costs make the pathfinder prefer safer terrain. Classification follows
    // vanilla's WalkNodeEvaluator: the node's bounding-box path type supplies the malus, and a floor whose own type
    // has a negative malus (lava, cactus, fences, powder snow) rejects the node.
    // The search's view of this mob, created once: a path cost reads it for every cell it checks.
    private MobPathBody? pathBody;
    internal MobPathBody PathBody => pathBody ??= new MobPathBody(this);

    protected internal virtual float GetPathCost(Vector feetPosition, IBlock feet, IBlock floor)
    {
        // Solid ground is BLOCKED (a negative malus for the mob's own cell), so only hazard floors reject.
        var floorType = PathTypeClassifier.Base(floor.GetHashCode());
        if (floorType != PathType.BLOCKED && GetPathfindingMalus(floorType) < 0)
            return -1;
        return GetPathfindingMalus(PathTypeClassifier.ForNode(PathBody, feetPosition));
    }
}
