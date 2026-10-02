namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Scatters blocks (leaf litter) on the ground around the trunk base: <see cref="Tries"/> random spots in the trunk
/// footprint grown by <see cref="Radius"/> horizontally and <see cref="Height"/> vertically.
/// </summary>
[ConfiguredFeatureProperty("minecraft:place_on_ground")]
public sealed class PlaceOnGroundDecorator : TreeDecorator
{
    public int Tries { get; init; } = 128;

    public int Radius { get; init; } = 2;

    public int Height { get; init; } = 1;

    public required IBlockStateProvider BlockStateProvider { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var lowest = context.GetLowestTrunkOrRoot();
        if (lowest.Count == 0)
            return;

        var first = lowest[0];
        var y = first.Y;
        int minX = first.X, maxX = first.X, minZ = first.Z, maxZ = first.Z;
        for (var i = 0; i < lowest.Count; i++)
        {
            var position = lowest[i];
            if (position.Y != y)
                continue;

            minX = Math.Min(minX, position.X);
            maxX = Math.Max(maxX, position.X);
            minZ = Math.Min(minZ, position.Z);
            maxZ = Math.Max(maxZ, position.Z);
        }

        var random = context.Random;
        for (var i = 0; i < this.Tries; i++)
        {
            var spotX = random.NextIntBetweenInclusive(minX - this.Radius, maxX + this.Radius);
            var spotY = random.NextIntBetweenInclusive(y - this.Height, y + this.Height);
            var spotZ = random.NextIntBetweenInclusive(minZ - this.Radius, maxZ + this.Radius);
            this.AttemptToPlaceBlockAbove(context, new Vector(spotX, spotY, spotZ));
        }
    }

    private void AttemptToPlaceBlockAbove(TreeDecoratorContext context, Vector ground)
    {
        var above = ground + Vector.Up;
        var aboveBlock = context.Level.GetBlock(above);
        if ((aboveBlock.IsAir || TreeBlocks.IsVine(aboveBlock))
            && TreeBlocks.IsSolidRender(context.Level.GetBlock(ground))
            && context.Level.GetHeight(HeightmapType.MotionBlockingNoLeaves, ground.X, ground.Z) <= above.Y)
        {
            context.SetBlock(above, this.BlockStateProvider.GetState(context.Random, above));
        }
    }
}
