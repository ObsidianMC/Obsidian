using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Port of vanilla's <c>DripstoneUtils</c>, shared by the dripstone features.
/// </summary>
internal static class DripstoneUtils
{
    private static readonly BlockSet dripstoneReplaceable = new("#minecraft:dripstone_replaceable_blocks");

    private static IBlock DripstoneBlock => field ??= BlocksRegistry.Get(Material.DripstoneBlock);

    private static IBlock PointedDripstone => field ??= BlocksRegistry.Get(Material.PointedDripstone);

    /// <summary>
    /// Vanilla <c>getDripstoneHeight</c>: the height of a large dripstone at <paramref name="radius"/> from its axis.
    /// </summary>
    public static double GetDripstoneHeight(double radius, double maxRadius, double scale, double bluntness)
    {
        if (radius < bluntness)
            radius = bluntness;

        var normalized = radius / maxRadius * 0.384;
        var a = 0.75 * Math.Pow(normalized, 1.3333333333333333);
        var b = Math.Pow(normalized, 0.6666666666666666);
        var c = 0.3333333333333333 * Math.Log(normalized);
        var height = Math.Max(scale * (a - b - c), 0.0);
        return height / 0.384 * maxRadius;
    }

    /// <summary>
    /// Vanilla <c>isCircleMostlyEmbeddedInStone</c>: the center and points around a circle of <paramref name="radius"/> are
    /// all solid (not air, water or lava).
    /// </summary>
    public static bool IsCircleMostlyEmbeddedInStone(IWorldGenLevel level, Vector center, int radius)
    {
        if (IsEmptyOrWaterOrLava(level.GetBlock(center)))
            return false;

        var step = 6.0f / radius;
        for (var angle = 0.0f; angle < (float)(Math.PI * 2); angle += step)
        {
            var dx = (int)(Mth.Cos(angle) * radius);
            var dz = (int)(Mth.Sin(angle) * radius);
            if (IsEmptyOrWaterOrLava(level.GetBlock(center + new Vector(dx, 0, dz))))
                return false;
        }

        return true;
    }

    public static bool IsEmptyOrWater(IBlock block) => block.IsAir || block.Material == Material.Water;

    public static bool IsNeitherEmptyNorWater(IBlock block) => !block.IsAir && block.Material != Material.Water;

    public static bool IsEmptyOrWaterOrLava(IBlock block) => block.IsAir || block.Material is Material.Water or Material.Lava;

    public static bool IsDripstoneBase(IBlock block) => block.Material == Material.DripstoneBlock || dripstoneReplaceable.Contains(block);

    public static bool IsDripstoneBaseOrLava(IBlock block) => IsDripstoneBase(block) || block.Material == Material.Lava;

    /// <summary>
    /// Vanilla <c>growPointedDripstone</c>: a base-to-tip column of <paramref name="length"/> pointed dripstone growing toward
    /// <paramref name="direction"/>, only if the block behind the start is a dripstone base.
    /// </summary>
    public static void GrowPointedDripstone(IWorldGenLevel level, Vector position, BlockFace direction, int length, bool mergedTip)
    {
        if (!IsDripstoneBase(level.GetBlock(position.Offset(direction.Opposite()))))
            return;

        foreach (var thickness in BaseToTipColumn(length, mergedTip))
        {
            var state = PointedDripstone
                .WithProperty("vertical_direction", direction == BlockFace.Up ? "up" : "down")
                .WithProperty("thickness", thickness)
                .WithProperty("waterlogged", FeatureHelpers.IsWaterFluid(level.GetBlock(position)));

            level.SetBlock(position, state);
            position = position.Offset(direction);
        }
    }

    /// <summary>
    /// Vanilla <c>placeDripstoneBlockIfPossible</c>: turns a dripstone-replaceable block into a dripstone block.
    /// </summary>
    public static bool PlaceDripstoneBlockIfPossible(IWorldGenLevel level, Vector position)
    {
        if (!dripstoneReplaceable.Contains(level.GetBlock(position)))
            return false;

        level.SetBlock(position, DripstoneBlock);
        return true;
    }

    // buildBaseToTipColumn: base, middles, frustum, tip.
    private static IEnumerable<string> BaseToTipColumn(int length, bool mergedTip)
    {
        if (length >= 3)
        {
            yield return "base";

            for (var i = 0; i < length - 3; i++)
                yield return "middle";
        }

        if (length >= 2)
            yield return "frustum";

        if (length >= 1)
            yield return mergedTip ? "tip_merge" : "tip";
    }
}
