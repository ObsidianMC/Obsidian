namespace Obsidian.WorldData.Features;

/// <summary>
/// Places one block if it can survive there, like vanilla's SimpleBlockFeature. Double plants place both halves.
/// </summary>
[ConfiguredFeatureClass("minecraft:simple_block")]
public sealed class SimpleBlockFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:simple_block";

    public required IBlockStateProvider ToPlace { get; init; }

    /// <summary>
    /// Schedules a block tick after placing (used by fluids-adjacent blocks); has no effect on the placed blocks themselves.
    /// </summary>
    public bool ScheduleTick { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var state = this.ToPlace.GetState(context.Random, origin);
        if (!state.CanSurvive(level, origin))
            return false;

        var blockClass = state.BlockClass();
        if (blockClass is "DoublePlantBlock" or "TallFlowerBlock" or "TallSeagrassBlock" or "SmallDripleafBlock")
        {
            if (!level.GetBlock(origin + Vector.Up).IsAir)
                return false;

            PlaceDoublePlant(level, state, origin);
        }
        else if (blockClass == "MossyCarpetBlock")
        {
            // Vanilla MossyCarpetBlock.placeAt also grows random side faces using the region's random, which
            // Obsidian's level doesn't expose; the base carpet is placed without them.
            level.SetBlock(origin, state.WithProperty("bottom", true));
        }
        else
        {
            level.SetBlock(origin, state);
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>DoublePlantBlock.placeAt</c>: lower half at <paramref name="position"/>, upper half above, each copying
    /// waterlogging from any water (source or flowing) already there, like <c>isWaterAt</c>.
    /// </summary>
    internal static void PlaceDoublePlant(IWorldGenLevel level, IBlock state, Vector position)
    {
        var above = position + Vector.Up;
        level.SetBlock(position, CopyWaterlogged(level, position, state.WithProperty("half", "lower")));
        level.SetBlock(above, CopyWaterlogged(level, above, state.WithProperty("half", "upper")));
    }

    private static IBlock CopyWaterlogged(IWorldGenLevel level, Vector position, IBlock state) =>
        state.HasProperty("waterlogged") ? state.WithProperty("waterlogged", FeatureHelpers.IsWaterFluid(level.GetBlock(position))) : state;
}
