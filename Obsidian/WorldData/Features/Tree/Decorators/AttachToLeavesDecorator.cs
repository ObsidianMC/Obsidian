using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Decorator that attaches blocks (like mangrove propagules or vines) to tree leaves.
/// Uses exclusion zones to prevent clustering and requires empty space in the attachment direction.
/// </summary>
[TreeProperty("minecraft:attached_to_leaves")]
public sealed class AttachToLeavesDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:attached_to_leaves";

    [Range(0, 16)]
    public required int ExclusionRadiusXZ { get; set; }

    [Range(0, 16)]
    public required int ExclusionRadiusY { get; set; }

    [Range(1, 16)]
    public required int RequiredEmptyBlocks { get; set; }

    public required IBlockStateProvider BlockProvider { get; set; }

    /// <summary>
    /// Directions to generate. 
    /// </summary>
    /// <remarks>
    /// Cannot be empty. 
    /// Must be up, down, north, south, west, or east.
    /// </remarks>
    public List<string> Directions { get; } = [];

    public override async ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions)
    {
        if (foliagePositions.Count == 0)
            return;

        var world = context.World;
        var random = context.Random;
        var placementBlacklist = new HashSet<Vector>();

        // Convert string directions to vectors
        var directionVectors = Directions.Select(ParseDirection).ToList();
        if (directionVectors.Count == 0)
            return;

        // Shuffle leaves for random placement
        var shuffledLeaves = foliagePositions.OrderBy(_ => random.Next()).ToList();

        foreach (var leafPos in shuffledLeaves)
        {
            // Pick a random direction
            var direction = directionVectors[random.Next(directionVectors.Count)];
            var placementPos = leafPos + direction;

            // Skip if in exclusion zone or fails probability check
            if (placementBlacklist.Contains(placementPos))
                continue;

            if (random.NextSingle() >= Probability)
                continue;

            // Check if there are enough empty blocks in the chosen direction
            if (!await HasRequiredEmptyBlocks(world, leafPos, direction))
                continue;

            // Place the block
            var block = BlockProvider.Get();
            await world.SetBlockAsync(placementPos, block);

            // Add exclusion zone around this placement
            AddExclusionZone(placementBlacklist, placementPos);
        }
    }

    /// <summary>
    /// Checks if there are the required number of empty (air) blocks in the given direction from the leaf.
    /// </summary>
    private async ValueTask<bool> HasRequiredEmptyBlocks(IWorld world, Vector leafPos, Vector direction)
    {
        for (int i = 1; i <= RequiredEmptyBlocks; i++)
        {
            var offsetPos = leafPos + (direction * i);
            var block = await world.GetBlockAsync(offsetPos);

            if (!block.IsAir)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Adds all positions within the exclusion radius to the blacklist.
    /// </summary>
    private void AddExclusionZone(HashSet<Vector> blacklist, Vector center)
    {
        // Create bounding box for exclusion zone
        var corner1 = center + (-ExclusionRadiusXZ, -ExclusionRadiusY, -ExclusionRadiusXZ);
        var corner2 = center + (ExclusionRadiusXZ, ExclusionRadiusY, ExclusionRadiusXZ);

        // Add all positions in the box
        for (int x = corner1.X; x <= corner2.X; x++)
        {
            for (int y = corner1.Y; y <= corner2.Y; y++)
            {
                for (int z = corner1.Z; z <= corner2.Z; z++)
                {
                    blacklist.Add(new Vector(x, y, z));
                }
            }
        }
    }

    /// <summary>
    /// Parses a direction string to a Vector.
    /// </summary>
    private static Vector ParseDirection(string direction)
    {
        return direction.ToLowerInvariant() switch
        {
            "up" => Vector.Up,
            "down" => Vector.Down,
            "north" => Vector.North,
            "south" => Vector.South,
            "east" => Vector.East,
            "west" => Vector.West,
            _ => Vector.Down // Default to down
        };
    }
}
