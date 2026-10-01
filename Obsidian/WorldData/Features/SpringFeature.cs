namespace Obsidian.WorldData.Features;

/// <summary>
/// A single water or lava source in a wall, placed only where it has exactly <see cref="RockCount"/> valid neighbors and
/// <see cref="HoleCount"/> open sides, like vanilla's SpringFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:spring_feature")]
public sealed class SpringFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:spring_feature";

    /// <summary>
    /// The fluid state (e.g. <c>minecraft:lava</c> with <c>falling</c>), turned into its liquid block when placed.
    /// </summary>
    public required SimpleBlockState State { get; init; }

    public bool RequiresBlockBelow { get; init; } = true;

    public int RockCount { get; init; } = 4;

    public int HoleCount { get; init; } = 1;

    public required BlockSet ValidBlocks { get; init; }

    private IBlock LiquidBlock => field ??= ToLiquidBlock(this.State);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        if (!this.ValidBlocks.Contains(level.GetBlock(origin + Vector.Up)))
            return false;

        if (this.RequiresBlockBelow && !this.ValidBlocks.Contains(level.GetBlock(origin + Vector.Down)))
            return false;

        var current = level.GetBlock(origin);
        if (!current.IsAir && !this.ValidBlocks.Contains(current))
            return false;

        var rocks = 0;
        var holes = 0;
        foreach (var offset in (ReadOnlySpan<Vector>)[Vector.West, Vector.East, Vector.North, Vector.South, Vector.Down])
        {
            var neighbor = level.GetBlock(origin + offset);
            if (this.ValidBlocks.Contains(neighbor))
                rocks++;

            if (neighbor.IsAir)
                holes++;
        }

        if (rocks != this.RockCount || holes != this.HoleCount)
            return false;

        level.SetBlock(origin, this.LiquidBlock);
        level.ScheduleFluidTick(origin);
        return true;
    }

    /// <summary>
    /// Vanilla <c>FluidState.createLegacyBlock</c>: sources become level 0; flowing fluids use
    /// <c>8 - min(amount, 8)</c> plus 8 when falling.
    /// </summary>
    private static IBlock ToLiquidBlock(SimpleBlockState fluid)
    {
        var flowing = fluid.Name.StartsWith("minecraft:flowing_", StringComparison.Ordinal);
        var blockName = flowing ? "minecraft:" + fluid.Name["minecraft:flowing_".Length..] : fluid.Name;

        var level = 0;
        if (flowing)
        {
            var amount = int.Parse(fluid.Properties.GetValueOrDefault("level", "8"));
            var falling = fluid.Properties.GetValueOrDefault("falling") == "true";
            level = 8 - Math.Min(amount, 8) + (falling ? 8 : 0);
        }

        return BlockStateProperties.GetState(blockName, new Dictionary<string, string> { ["level"] = level.ToString() });
    }
}
