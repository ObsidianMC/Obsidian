namespace Obsidian.WorldData.Features;

/// <summary>
/// A column of stacked layers (cave vines, dripleaf stems, sugar cane...) growing in <see cref="Direction"/>, like vanilla's
/// BlockColumnFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:block_column")]
public sealed class BlockColumnFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:block_column";

    public required ImmutableArray<BlockColumnLayer> Layers { get; init; }

    public required BlockFace Direction { get; init; }

    /// <summary>
    /// Each block past the origin must pass this, otherwise the column is shortened.
    /// </summary>
    public required IBlockPredicate AllowedPlacement { get; init; }

    /// <summary>
    /// When shortened, remove height from the first layers (keeping the tip) instead of the last ones.
    /// </summary>
    public required bool PrioritizeTip { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        if (!level.EnsureCanWrite(context.Origin))
            return false;

        var heights = new int[this.Layers.Length];
        var total = 0;
        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = this.Layers[i].Height.Sample(random);
            total += heights[i];
        }

        if (total == 0)
            return false;

        var probe = context.Origin.Offset(this.Direction);
        for (var i = 0; i < total; i++)
        {
            if (!this.AllowedPlacement.Test(level, probe))
            {
                Truncate(heights, total, i, this.PrioritizeTip);
                break;
            }

            probe = probe.Offset(this.Direction);
        }

        var position = context.Origin;
        for (var i = 0; i < heights.Length; i++)
        {
            var layer = this.Layers[i];
            for (var j = 0; j < heights[i]; j++)
            {
                level.SetBlock(position, layer.Provider.GetState(random, position));
                position = position.Offset(this.Direction);
            }
        }

        return true;
    }

    private static void Truncate(int[] heights, int total, int allowed, bool prioritizeTip)
    {
        var toRemove = total - allowed;
        var step = prioritizeTip ? 1 : -1;
        var start = prioritizeTip ? 0 : heights.Length - 1;
        var end = prioritizeTip ? heights.Length : -1;

        for (var i = start; i != end && toRemove > 0; i += step)
        {
            var removed = Math.Min(heights[i], toRemove);
            toRemove -= removed;
            heights[i] -= removed;
        }
    }
}

/// <summary>
/// One layer of a <see cref="BlockColumnFeature"/>.
/// </summary>
public sealed class BlockColumnLayer
{
    public required IIntProvider Height { get; init; }

    public required IBlockStateProvider Provider { get; init; }
}
