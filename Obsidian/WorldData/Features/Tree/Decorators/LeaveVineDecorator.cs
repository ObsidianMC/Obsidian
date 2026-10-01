namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Hangs vines (up to 5 long) from the sides of leaves, each side with <see cref="Probability"/>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:leave_vine")]
public sealed class LeaveVineDecorator : TreeDecorator
{
    public required float Probability { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var random = context.Random;
        foreach (var leaf in context.Leaves)
        {
            if (random.NextFloat() < this.Probability)
                AddHangingVineIfAir(context, leaf + Vector.West, BlockFace.East);

            if (random.NextFloat() < this.Probability)
                AddHangingVineIfAir(context, leaf + Vector.East, BlockFace.West);

            if (random.NextFloat() < this.Probability)
                AddHangingVineIfAir(context, leaf + Vector.North, BlockFace.South);

            if (random.NextFloat() < this.Probability)
                AddHangingVineIfAir(context, leaf + Vector.South, BlockFace.North);
        }
    }

    private static void AddHangingVineIfAir(TreeDecoratorContext context, Vector position, BlockFace face)
    {
        if (!context.IsAir(position))
            return;

        context.PlaceVine(position, face);
        var below = position + Vector.Down;
        for (var remaining = 4; context.IsAir(below) && remaining > 0; remaining--)
        {
            context.PlaceVine(below, face);
            below += Vector.Down;
        }
    }
}
