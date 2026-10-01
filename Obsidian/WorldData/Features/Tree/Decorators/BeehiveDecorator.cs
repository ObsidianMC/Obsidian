namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// With <see cref="Probability"/>, attaches a bee nest (facing south) to the trunk just below the leaves.
/// </summary>
/// <remarks>
/// Vanilla also fills the nest's block entity with 2-3 bees. Obsidian has no beehive block entity yet, so only the
/// block is placed, but the random values vanilla draws for the bees are still consumed to keep later placements in sync.
/// </remarks>
[ConfiguredFeatureProperty("minecraft:beehive")]
public sealed class BeehiveDecorator : TreeDecorator
{
    private static readonly IBlock beeNest = BlockStateProperties.GetState("minecraft:bee_nest").WithProperty("facing", "south");

    /// <summary>Sides a nest may attach to: every horizontal side except north, so the nest's south face stays clear.</summary>
    private static readonly BlockFace[] spawnDirections = [BlockFace.East, BlockFace.South, BlockFace.West];

    public required float Probability { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var leaves = context.Leaves;
        var logs = context.Logs;
        if (logs.Count == 0)
            return;

        var random = context.Random;
        if (random.NextFloat() >= this.Probability)
            return;

        var nestY = leaves.Count > 0
            ? Math.Max(leaves[0].Y - 1, logs[0].Y + 1)
            : Math.Min(logs[0].Y + 1 + random.NextInt(3), logs[^1].Y);

        var candidates = new List<Vector>();
        foreach (var log in logs)
        {
            if (log.Y != nestY)
                continue;

            foreach (var direction in spawnDirections)
                candidates.Add(log + direction.ToVector());
        }

        if (candidates.Count == 0)
            return;

        TreeDecoratorContext.Shuffle(candidates, random);
        foreach (var candidate in candidates)
        {
            if (!context.IsAir(candidate) || !context.IsAir(candidate + Vector.South))
                continue;

            context.SetBlock(candidate, beeNest);

            // Vanilla only finds the block entity (and draws the occupants: 2-3 bees with random ticks in the hive)
            // if the nest was actually written.
            if (context.Level.GetBlock(candidate).RegistryId == beeNest.RegistryId)
            {
                var bees = 2 + random.NextInt(2);
                for (var i = 0; i < bees; i++)
                    random.NextInt(599);
            }

            return;
        }
    }
}
