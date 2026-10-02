namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Attaches blocks (hanging mangrove propagules) next to random leaves, keeping attached blocks apart.
/// </summary>
[ConfiguredFeatureProperty("minecraft:attached_to_leaves")]
public sealed class AttachedToLeavesDecorator : TreeDecorator
{
    public required float Probability { get; init; }

    /// <summary>Horizontal radius around a placed block in which no other block is attached.</summary>
    public required int ExclusionRadiusXz { get; init; }

    public required int ExclusionRadiusY { get; init; }

    public required IBlockStateProvider BlockProvider { get; init; }

    /// <summary>Air blocks required in the attach direction, starting next to the leaf.</summary>
    public required int RequiredEmptyBlocks { get; init; }

    public required ImmutableArray<BlockFace> Directions { get; init; }

    public override void Place(TreeDecoratorContext context)
    {
        var excluded = new HashSet<Vector>();
        var random = context.Random;
        var leaves = context.Leaves.ToList();
        TreeDecoratorContext.Shuffle(leaves, random);

        foreach (var leaf in leaves)
        {
            var direction = this.Directions[random.NextInt(this.Directions.Length)];
            var position = leaf + direction.ToVector();
            if (excluded.Contains(position) || random.NextFloat() >= this.Probability || !this.HasRequiredEmptyBlocks(context, leaf, direction))
                continue;

            for (var x = -this.ExclusionRadiusXz; x <= this.ExclusionRadiusXz; x++)
            {
                for (var y = -this.ExclusionRadiusY; y <= this.ExclusionRadiusY; y++)
                {
                    for (var z = -this.ExclusionRadiusXz; z <= this.ExclusionRadiusXz; z++)
                        excluded.Add(position + (x, y, z));
                }
            }

            context.SetBlock(position, this.BlockProvider.GetState(random, position));
        }
    }

    private bool HasRequiredEmptyBlocks(TreeDecoratorContext context, Vector leaf, BlockFace direction)
    {
        var step = direction.ToVector();
        for (var i = 1; i <= this.RequiredEmptyBlocks; i++)
        {
            if (!context.IsAir(leaf + step * i))
                return false;
        }

        return true;
    }
}
