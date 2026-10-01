namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Pale oak decoration: maybe a <c>minecraft:pale_moss_patch</c> at the trunk base, then pale hanging moss under logs
/// and leaves.
/// </summary>
[ConfiguredFeatureProperty("minecraft:pale_moss")]
public sealed class PaleMossDecorator : TreeDecorator
{
    private static readonly IBlock hangingMoss = BlockStateProperties.GetState("minecraft:pale_hanging_moss");

    public required float LeavesProbability { get; init; }

    public required float TrunkProbability { get; init; }

    public required float GroundProbability { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var random = context.Random;
        var shuffledLogs = context.Logs.ToList();
        TreeDecoratorContext.Shuffle(shuffledLogs, random);
        if (shuffledLogs.Count == 0)
            return;

        // Collections.min: the first lowest log in shuffled order.
        var lowest = shuffledLogs[0];
        foreach (var log in shuffledLogs)
        {
            if (log.Y < lowest.Y)
                lowest = log;
        }

        if (random.NextFloat() < this.GroundProbability)
        {
            ConfiguredFeatures.Vegetation.PaleMossPatch.Place(new FeatureContext
            {
                Level = context.Level,
                Origin = lowest + Vector.Up,
                Random = random,
                Generation = context.Generation
            });
        }

        foreach (var log in context.Logs)
        {
            if (random.NextFloat() < this.TrunkProbability)
                AddMossHangerIfAir(context, log + Vector.Down);
        }

        foreach (var leaf in context.Leaves)
        {
            if (random.NextFloat() < this.LeavesProbability)
                AddMossHangerIfAir(context, leaf + Vector.Down);
        }
    }

    private static void AddMossHangerIfAir(TreeDecoratorContext context, Vector position)
    {
        if (!context.IsAir(position))
            return;

        // Grow down while there is air below, stopping early with a 50% chance per block.
        while (context.IsAir(position + Vector.Down) && !(context.Random.NextFloat() < 0.5))
        {
            context.SetBlock(position, hangingMoss.WithProperty("tip", false));
            position += Vector.Down;
        }

        context.SetBlock(position, hangingMoss.WithProperty("tip", true));
    }
}
