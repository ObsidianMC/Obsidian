using Obsidian.API.World;

namespace Obsidian.API.AI;

/// <summary>
/// The body a path search runs for: its position, size, world view and movement capabilities. Mobs supply one through
/// their navigator; plugins may supply their own to compute paths for any body (see <see cref="IPathfinder"/>).
/// </summary>
public interface IPathBody
{
    /// <summary>
    /// Gets the body's world position in blocks.
    /// </summary>
    VectorD Position { get; }

    /// <summary>
    /// Gets the body's dimensions used for path clearance checks.
    /// </summary>
    EntityDimension Dimension { get; }

    /// <summary>
    /// Gets the level in which the body searches for a path.
    /// </summary>
    ILevel Level { get; }

    /// <summary>
    /// Gets the terrain view used for collision and fluid checks.
    /// </summary>
    IPathTerrain Terrain { get; }

    /// <summary>The body's step height (<c>minecraft:generic.step_height</c>).</summary>
    float StepHeight { get; }

    /// <summary>The fall distance the search allows.</summary>
    int MaxFall { get; }

    /// <summary>The range the search may explore from the start.</summary>
    float FollowRange { get; }

    /// <summary>
    /// Gets whether path evaluation may pass through openable wooden doors.
    /// </summary>
    bool CanOpenDoors { get; }

    /// <summary>
    /// Gets whether ground navigation may start at the water surface.
    /// </summary>
    bool CanFloat { get; }

    /// <summary>
    /// Gets whether underwater navigation searches the swimming volume.
    /// </summary>
    bool SwimmingNavigation { get; }

    /// <summary>
    /// Gets whether navigation searches a three-dimensional flying volume.
    /// </summary>
    bool FlyingNavigation { get; }

    /// <summary>
    /// Gets whether the body is currently in water.
    /// </summary>
    bool InWater { get; }

    /// <summary>The malus (path cost penalty) for a node type; negative values are hazards the search avoids.</summary>
    float Malus(PathNodeType type);

    /// <summary>The cost of standing at a cell, given its feet and floor blocks; negative rejects the cell.</summary>
    float NodeCost(Vector feetPosition, IBlock feet, IBlock floor);
}

/// <summary>The block view a path search reads.</summary>
public interface IPathTerrain
{
    /// <summary>
    /// Gets the block at world block coordinates, or <c>null</c> when unavailable.
    /// </summary>
    IBlock? GetBlock(Vector position);

    /// <summary>
    /// Tests whether a world-space box is clear of block collision, optionally ignoring wooden doors.
    /// </summary>
    bool IsFree(BoundingBox bounds, bool ignoreWoodenDoors = false);

    /// <summary>
    /// Tests whether a world point is below the water surface when true, or the lava surface when false.
    /// </summary>
    bool IsInFluid(VectorD point, bool water);
}
