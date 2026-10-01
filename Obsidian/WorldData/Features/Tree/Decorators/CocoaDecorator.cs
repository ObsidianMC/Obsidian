namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// With <see cref="Probability"/>, grows cocoa pods (random age) on the sides of the lowest three log layers.
/// </summary>
[ConfiguredFeatureProperty("minecraft:cocoa")]
public sealed class CocoaDecorator : TreeDecorator
{
    private static readonly IBlock cocoa = BlockStateProperties.GetState("minecraft:cocoa");

    public required float Probability { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var random = context.Random;
        if (random.NextFloat() >= this.Probability)
            return;

        var logs = context.Logs;
        if (logs.Count == 0)
            return;

        var lowestY = logs[0].Y;
        foreach (var log in logs)
        {
            if (log.Y - lowestY > 2)
                continue;

            foreach (var facing in TreeDirections.Horizontal)
            {
                if (random.NextFloat() > 0.25f)
                    continue;

                // The pod sits on the opposite side of the log and faces it.
                var opposite = facing.Opposite().ToVector();
                var position = log + (opposite.X, 0, opposite.Z);
                if (context.IsAir(position))
                {
                    var block = cocoa.WithProperty("age", random.NextInt(3)).WithProperty("facing", facing.PropertyName());
                    context.SetBlock(position, block);
                }
            }
        }
    }
}
