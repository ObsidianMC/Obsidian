using Obsidian.API.World;

namespace Obsidian.API.AI;

/// <summary>
/// The body a path search runs for: its position, size, world view and movement capabilities. Mobs supply one through
/// their navigator; plugins may supply their own to compute paths for any body (see <see cref="IPathfinder"/>).
/// </summary>
public interface IPathBody
{
    VectorD Position { get; }

    EntityDimension Dimension { get; }

    ILevel Level { get; }

    IPathTerrain Terrain { get; }

    /// <summary>The body's step height (<c>minecraft:generic.step_height</c>).</summary>
    float StepHeight { get; }

    /// <summary>The fall distance the search allows.</summary>
    int MaxFall { get; }

    /// <summary>The range the search may explore from the start.</summary>
    float FollowRange { get; }

    bool CanOpenDoors { get; }

    bool CanFloat { get; }

    bool SwimmingNavigation { get; }

    bool FlyingNavigation { get; }

    bool InWater { get; }

    /// <summary>The malus (path cost penalty) for a node type; negative values are hazards the search avoids.</summary>
    float Malus(PathNodeType type);

    /// <summary>The cost of standing at a cell, given its feet and floor blocks; negative rejects the cell.</summary>
    float NodeCost(Vector feetPosition, IBlock feet, IBlock floor);
}

/// <summary>The block view a path search reads.</summary>
public interface IPathTerrain
{
    IBlock? GetBlock(Vector position);

    bool IsFree(BoundingBox bounds, bool ignoreWoodenDoors = false);

    bool IsInFluid(VectorD point, bool water);
}
