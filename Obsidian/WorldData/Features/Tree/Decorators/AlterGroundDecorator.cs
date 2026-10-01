namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Replaces the ground around the trunk base with <see cref="Provider"/> in rounded patches (podzol under mega spruces).
/// </summary>
[ConfiguredFeatureProperty("minecraft:alter_ground")]
public sealed class AlterGroundDecorator : TreeDecorator
{
    public required IBlockStateProvider Provider { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var lowest = context.GetLowestTrunkOrRoot();
        if (lowest.Count == 0)
            return;

        var baseY = lowest[0].Y;
        foreach (var position in lowest)
        {
            if (position.Y != baseY)
                continue;

            this.PlaceCircle(context, position + (-1, 0, -1));
            this.PlaceCircle(context, position + (2, 0, -1));
            this.PlaceCircle(context, position + (-1, 0, 2));
            this.PlaceCircle(context, position + (2, 0, 2));

            for (var i = 0; i < 5; i++)
            {
                // A random spot on the edge of an 8x8 square around the trunk.
                var spot = context.Random.NextInt(64);
                var x = spot % 8;
                var z = spot / 8;
                if (x == 0 || x == 7 || z == 0 || z == 7)
                    this.PlaceCircle(context, position + (-3 + x, 0, -3 + z));
            }
        }
    }

    private void PlaceCircle(TreeDecoratorContext context, Vector center)
    {
        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dz = -2; dz <= 2; dz++)
            {
                if (Math.Abs(dx) != 2 || Math.Abs(dz) != 2)
                    this.PlaceBlockAt(context, center + (dx, 0, dz));
            }
        }
    }

    /// <summary>Replaces the first dirt block from 2 above to 3 below, stopping at a non-air block below the start.</summary>
    private void PlaceBlockAt(TreeDecoratorContext context, Vector position)
    {
        for (var dy = 2; dy >= -3; dy--)
        {
            var candidate = position + (0, dy, 0);
            if (TreeBlocks.IsGrassOrDirt(context.Level, candidate))
            {
                // Vanilla samples the provider at the column position, not the replaced block.
                context.SetBlock(candidate, this.Provider.GetState(context.Random, position));
                break;
            }

            if (!context.IsAir(candidate) && dy < 0)
                break;
        }
    }
}
