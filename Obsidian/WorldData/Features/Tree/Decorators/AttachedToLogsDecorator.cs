namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Attaches blocks next to random logs in one of <see cref="Directions"/>, each with <see cref="Probability"/>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:attached_to_logs")]
public sealed class AttachedToLogsDecorator : TreeDecorator
{
    public required float Probability { get; init; }

    public required IBlockStateProvider BlockProvider { get; init; }

    public required ImmutableArray<BlockFace> Directions { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var random = context.Random;
        var logs = context.Logs.ToList();
        TreeDecoratorContext.Shuffle(logs, random);

        foreach (var log in logs)
        {
            var direction = this.Directions[random.NextInt(this.Directions.Length)];
            var position = log + direction.ToVector();
            if (random.NextFloat() <= this.Probability && context.IsAir(position))
                context.SetBlock(position, this.BlockProvider.GetState(random, position));
        }
    }
}
