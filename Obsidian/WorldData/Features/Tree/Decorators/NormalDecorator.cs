using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Decorator that places beehives on tree trunks with configurable probability.
/// </summary>
[TreeProperty("minecraft:beehive")]
public sealed class NormalDecorator : DecoratorBase
{
    public override required string Type { get; init; }

    private static readonly Vector WorldgenFacing = Vector.South;
    private static readonly Vector[] SpawnDirections = new[]
    {
        Vector.North,
        Vector.East,
        Vector.South,
        Vector.West
    }.Where(dir => dir != -WorldgenFacing).ToArray();

    public override async ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions)
    {
        if (trunkPositions.Count == 0)
            return;

        var random = context.Random;
        var world = context.World;

        // Check probability
        if (random.NextSingle() >= Probability)
            return;

        // Determine hive Y position
        int hiveY;
        if (foliagePositions.Count > 0)
        {
            // Place between first foliage and first trunk, but at least 1 above first trunk
            int firstFoliageY = foliagePositions.Min(pos => pos.Y);
            int firstTrunkY = trunkPositions.Min(pos => pos.Y);
            hiveY = Math.Max(firstFoliageY - 1, firstTrunkY + 1);
        }
        else
        {
            // Place 1-3 blocks above first trunk, but no higher than last trunk
            int firstTrunkY = trunkPositions.Min(pos => pos.Y);
            int lastTrunkY = trunkPositions.Max(pos => pos.Y);
            hiveY = Math.Min(firstTrunkY + 1 + random.Next(3), lastTrunkY);
        }

        // Find all trunk positions at the hive Y level
        var trunksAtHiveY = trunkPositions.Where(pos => pos.Y == hiveY).ToList();
        if (trunksAtHiveY.Count == 0)
            return;

        // Get all potential placement positions (adjacent to trunks in horizontal directions)
        var hivePlacements = trunksAtHiveY
            .SelectMany(pos => SpawnDirections.Select(dir => pos + dir))
            .ToList();

        if (hivePlacements.Count == 0)
            return;

        // Shuffle and find first valid position
        hivePlacements = hivePlacements.OrderBy(_ => random.Next()).ToList();

        foreach (var hivePos in hivePlacements)
        {
            var blockAtPos = await world.GetBlockAsync(hivePos);
            var blockInFront = await world.GetBlockAsync(hivePos + WorldgenFacing);

            // Check if position and front are air
            if (blockAtPos.IsAir && blockInFront.IsAir)
            {
                // Place beehive facing south
                var beehiveState = new SimpleBlockState
                {
                    Name = "minecraft:bee_nest",
                    Properties = new Dictionary<string, string>
                    {
                        { "facing", "south" },
                        { "honey_level", "0" }
                    }
                };

                var beehive = BlocksRegistry.GetFromSimpleState(beehiveState);
                await world.SetBlockAsync(hivePos, beehive);

                // TODO: Spawn 2-3 bees inside the hive using block entity data
                // This would require block entity support to store bee occupants

                break; // Only place one hive
            }
        }
    }
}
