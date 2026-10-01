using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A small pointed dripstone (1-2 long) hanging from or standing on dripstone, with a patch of dripstone blocks around its
/// base, like vanilla's PointedDripstoneFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:pointed_dripstone")]
public sealed class PointedDripstoneFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:pointed_dripstone";

    public float ChanceOfTallerDripstone { get; init; } = 0.2f;

    public float ChanceOfDirectionalSpread { get; init; } = 0.7f;

    public float ChanceOfSpreadRadius2 { get; init; } = 0.5f;

    public float ChanceOfSpreadRadius3 { get; init; } = 0.5f;

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var tip = GetTipDirection(level, origin, random);
        if (tip is null)
            return false;

        var direction = tip.Value;
        this.CreatePatchOfDripstoneBlocks(level, random, origin.Offset(direction.Opposite()));

        var length = random.NextFloat() < this.ChanceOfTallerDripstone && DripstoneUtils.IsEmptyOrWater(level.GetBlock(origin.Offset(direction)))
            ? 2
            : 1;
        DripstoneUtils.GrowPointedDripstone(level, origin, direction, length, false);
        return true;
    }

    private static BlockFace? GetTipDirection(IWorldGenLevel level, Vector position, IRandomSource random)
    {
        var ceiling = DripstoneUtils.IsDripstoneBase(level.GetBlock(position + Vector.Up));
        var floor = DripstoneUtils.IsDripstoneBase(level.GetBlock(position + Vector.Down));

        if (ceiling && floor)
            return random.NextBoolean() ? BlockFace.Down : BlockFace.Up;

        if (ceiling)
            return BlockFace.Down;

        return floor ? BlockFace.Up : null;
    }

    private void CreatePatchOfDripstoneBlocks(IWorldGenLevel level, IRandomSource random, Vector position)
    {
        DripstoneUtils.PlaceDripstoneBlockIfPossible(level, position);

        foreach (var face in FeatureHelpers.Horizontal)
        {
            if (random.NextFloat() > this.ChanceOfDirectionalSpread)
                continue;

            var first = position.Offset(face);
            DripstoneUtils.PlaceDripstoneBlockIfPossible(level, first);
            if (random.NextFloat() > this.ChanceOfSpreadRadius2)
                continue;

            var second = first.Offset(FeatureHelpers.RandomDirection(random));
            DripstoneUtils.PlaceDripstoneBlockIfPossible(level, second);
            if (random.NextFloat() > this.ChanceOfSpreadRadius3)
                continue;

            DripstoneUtils.PlaceDripstoneBlockIfPossible(level, second.Offset(FeatureHelpers.RandomDirection(random)));
        }
    }
}
