using Obsidian.Nbt;

namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// With <see cref="Probability"/>, attaches a bee nest (facing south) with 2-3 bees to the trunk just below the leaves.
/// </summary>
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

            // Like vanilla, the bees (and their draws) only go into a nest that was actually written.
            var hive = context.Level.GetBlockEntity(candidate) as DataBlockEntity;
            if (hive?.Id == "minecraft:beehive")
            {
                var beeCount = 2 + random.NextInt(2);
                var bees = new NbtList(NbtTagType.Compound, "bees");
                for (var i = 0; i < beeCount; i++)
                {
                    // BeehiveBlockEntity.Occupant.create: a fresh bee that has been in the hive for a random time.
                    bees.Add(new NbtCompound
                    {
                        new NbtCompound("entity_data") { new NbtTag<string>("id", "minecraft:bee") },
                        new NbtTag<int>("ticks_in_hive", random.NextInt(599)),
                        new NbtTag<int>("min_ticks_in_hive", 600)
                    });
                }

                hive.Set(bees);
            }

            return;
        }
    }
}
