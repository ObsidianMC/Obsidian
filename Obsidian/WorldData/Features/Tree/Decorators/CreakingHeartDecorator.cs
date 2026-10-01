namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// With <see cref="Probability"/>, replaces a log fully enclosed by logs with a dormant natural creaking heart.
/// </summary>
[ConfiguredFeatureProperty("minecraft:creaking_heart")]
public sealed class CreakingHeartDecorator : TreeDecorator
{
    private static readonly IBlock creakingHeart = BlockStateProperties.GetState("minecraft:creaking_heart")
        .WithProperty("creaking_heart_state", "dormant")
        .WithProperty("natural", true);

    public required float Probability { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var random = context.Random;
        if (context.Logs.Count == 0 || random.NextFloat() >= this.Probability)
            return;

        var logs = context.Logs.ToList();
        TreeDecoratorContext.Shuffle(logs, random);
        foreach (var log in logs)
        {
            if (TreeDirections.All.All(face => TreeBlocks.Logs.Contains(context.Level.GetBlock(log + face.ToVector()))))
            {
                context.SetBlock(log, creakingHeart);
                return;
            }
        }
    }
}
