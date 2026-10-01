using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A field of stalactites, stalagmites and dripstone blocks filling a cave area, sometimes with small pools, like vanilla's
/// DripstoneClusterFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:dripstone_cluster")]
public sealed class DripstoneClusterFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:dripstone_cluster";

    public required int FloorToCeilingSearchRange { get; init; }

    public required IIntProvider Height { get; init; }

    public required IIntProvider Radius { get; init; }

    public required int MaxStalagmiteStalactiteHeightDiff { get; init; }

    public required int HeightDeviation { get; init; }

    public required IIntProvider DripstoneBlockLayerThickness { get; init; }

    public required IFloatProvider Density { get; init; }

    public required IFloatProvider Wetness { get; init; }

    public required float ChanceOfDripstoneColumnAtMaxDistanceFromCenter { get; init; }

    public required int MaxDistanceFromEdgeAffectingChanceOfDripstoneColumn { get; init; }

    public required int MaxDistanceFromCenterAffectingHeightBias { get; init; }

    private static readonly BlockSet baseStoneOverworld = new("#minecraft:base_stone_overworld");

    private static IBlock Water => field ??= BlocksRegistry.Get(Material.Water);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !DripstoneUtils.IsEmptyOrWater(level.GetBlock(origin)))
            return false;

        var height = this.Height.Sample(random);
        var wetness = this.Wetness.Sample(random);
        var density = this.Density.Sample(random);
        var radiusX = this.Radius.Sample(random);
        var radiusZ = this.Radius.Sample(random);

        for (var dx = -radiusX; dx <= radiusX; dx++)
        {
            for (var dz = -radiusZ; dz <= radiusZ; dz++)
            {
                double chance = this.GetChanceOfStalagmiteOrStalactite(radiusX, radiusZ, dx, dz);
                this.PlaceColumn(level, random, origin + new Vector(dx, 0, dz), dx, dz, wetness, chance, height, density);
            }
        }

        return true;
    }

    private void PlaceColumn(IWorldGenLevel level, IRandomSource random, Vector position, int dx, int dz, float wetness, double chance,
        int height, float density)
    {
        var scanned = Column.Scan(level, position, this.FloorToCeilingSearchRange, DripstoneUtils.IsEmptyOrWater,
            DripstoneUtils.IsNeitherEmptyNorWater);
        if (scanned is null)
            return;

        var column = scanned.Value;
        var ceiling = column.Ceiling;
        var floor = column.Floor;
        if (ceiling is null && floor is null)
            return;

        var pool = random.NextFloat() < wetness;
        if (pool && floor is not null && CanPlacePool(level, position.AtY(floor.Value)))
        {
            level.SetBlock(position.AtY(floor.Value), Water);
            column = column with { Floor = floor.Value - 1 };
        }

        var poolFloor = column.Floor;

        var stalactiteRoll = random.NextDouble() < chance;
        int stalactiteHeight;
        if (ceiling is not null && stalactiteRoll && level.GetBlock(position.AtY(ceiling.Value)).Material != Material.Lava)
        {
            var thickness = this.DripstoneBlockLayerThickness.Sample(random);
            ReplaceBlocksWithDripstoneBlocks(level, position.AtY(ceiling.Value), thickness, BlockFace.Up);
            var maxHeight = poolFloor is not null ? Math.Min(height, ceiling.Value - poolFloor.Value) : height;
            stalactiteHeight = this.GetDripstoneHeight(random, dx, dz, density, maxHeight);
        }
        else
        {
            stalactiteHeight = 0;
        }

        var stalagmiteRoll = random.NextDouble() < chance;
        int stalagmiteHeight;
        if (poolFloor is not null && stalagmiteRoll && level.GetBlock(position.AtY(poolFloor.Value)).Material != Material.Lava)
        {
            var thickness = this.DripstoneBlockLayerThickness.Sample(random);
            ReplaceBlocksWithDripstoneBlocks(level, position.AtY(poolFloor.Value), thickness, BlockFace.Down);
            stalagmiteHeight = ceiling is not null
                ? Math.Max(0, stalactiteHeight + FeatureHelpers.RandomBetweenInclusive(random, -this.MaxStalagmiteStalactiteHeightDiff,
                    this.MaxStalagmiteStalactiteHeightDiff))
                : this.GetDripstoneHeight(random, dx, dz, density, height);
        }
        else
        {
            stalagmiteHeight = 0;
        }

        int down;
        int up;
        if (ceiling is not null && poolFloor is not null && ceiling.Value - stalactiteHeight <= poolFloor.Value + stalagmiteHeight)
        {
            // Overlapping stalactite and stalagmite meet at a random height between them.
            var bottom = poolFloor.Value;
            var top = ceiling.Value;
            var low = Math.Max(top - stalactiteHeight, bottom + 1);
            var high = Math.Min(bottom + stalagmiteHeight, top - 1);
            var meet = FeatureHelpers.RandomBetweenInclusive(random, low, high + 1);
            down = top - meet;
            up = meet - 1 - bottom;
        }
        else
        {
            down = stalactiteHeight;
            up = stalagmiteHeight;
        }

        var merge = random.NextBoolean() && down > 0 && up > 0 && column.Height == down + up;
        if (ceiling is not null)
            DripstoneUtils.GrowPointedDripstone(level, position.AtY(ceiling.Value - 1), BlockFace.Down, down, merge);

        if (poolFloor is not null)
            DripstoneUtils.GrowPointedDripstone(level, position.AtY(poolFloor.Value + 1), BlockFace.Up, up, merge);
    }

    private int GetDripstoneHeight(IRandomSource random, int dx, int dz, float density, int maxHeight)
    {
        if (random.NextFloat() > density)
            return 0;

        var distance = Math.Abs(dx) + Math.Abs(dz);
        var bias = (float)FeatureHelpers.ClampedMap(distance, 0.0, this.MaxDistanceFromCenterAffectingHeightBias, maxHeight / 2.0, 0.0);
        return (int)FeatureHelpers.ClampedNormal(random, bias, this.HeightDeviation, 0.0f, maxHeight);
    }

    private static bool CanPlacePool(IWorldGenLevel level, Vector position)
    {
        var state = level.GetBlock(position);
        if (state.Material is Material.Water or Material.DripstoneBlock or Material.PointedDripstone)
            return false;

        if (FeatureHelpers.IsWaterFluid(level.GetBlock(position + Vector.Up)))
            return false;

        foreach (var face in FeatureHelpers.Horizontal)
        {
            if (!CanBeAdjacentToWater(level.GetBlock(position.Offset(face))))
                return false;
        }

        return CanBeAdjacentToWater(level.GetBlock(position + Vector.Down));
    }

    private static bool CanBeAdjacentToWater(IBlock block) => baseStoneOverworld.Contains(block) || FeatureHelpers.IsWaterFluid(block);

    private static void ReplaceBlocksWithDripstoneBlocks(IWorldGenLevel level, Vector position, int thickness, BlockFace direction)
    {
        for (var i = 0; i < thickness; i++)
        {
            if (!DripstoneUtils.PlaceDripstoneBlockIfPossible(level, position))
                return;

            position = position.Offset(direction);
        }
    }

    // Float overload of Mth.clampedMap, like vanilla.
    private float GetChanceOfStalagmiteOrStalactite(int radiusX, int radiusZ, int dx, int dz)
    {
        var edgeDistance = Math.Min(radiusX - Math.Abs(dx), radiusZ - Math.Abs(dz));
        return FeatureHelpers.ClampedMap(edgeDistance, 0.0f, this.MaxDistanceFromEdgeAffectingChanceOfDripstoneColumn,
            this.ChanceOfDripstoneColumnAtMaxDistanceFromCenter, 1.0f);
    }
}
