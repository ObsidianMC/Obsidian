namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Covers the sides of every log with vines (2/3 chance per side), as on swamp and jungle trees.
/// </summary>
[ConfiguredFeatureProperty("minecraft:trunk_vine")]
public sealed class TrunkVineDecorator : TreeDecorator
{
    public override void Place(TreeDecoratorContext context)
    {
        var random = context.Random;
        foreach (var log in context.Logs)
        {
            // West, east, north, south; each vine faces back toward the log.
            if (random.NextInt(3) > 0)
                PlaceIfAir(context, log + Vector.West, BlockFace.East);

            if (random.NextInt(3) > 0)
                PlaceIfAir(context, log + Vector.East, BlockFace.West);

            if (random.NextInt(3) > 0)
                PlaceIfAir(context, log + Vector.North, BlockFace.South);

            if (random.NextInt(3) > 0)
                PlaceIfAir(context, log + Vector.South, BlockFace.North);
        }
    }

    private static void PlaceIfAir(TreeDecoratorContext context, Vector position, BlockFace face)
    {
        if (context.IsAir(position))
            context.PlaceVine(position, face);
    }
}
