using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A pair of huge stalactite and stalagmite cones made of dripstone blocks, optionally bent by "wind", like vanilla's
/// LargeDripstoneFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:large_dripstone")]
public sealed class LargeDripstoneFeature : ConfiguredFeatureBase
{
    private static readonly BlockSet baseStoneOverworld = new("#minecraft:base_stone_overworld");

    public override string Type => "minecraft:large_dripstone";

    public int FloorToCeilingSearchRange { get; init; } = 30;

    public required IIntProvider ColumnRadius { get; init; }

    public required IFloatProvider HeightScale { get; init; }

    public required float MaxColumnRadiusToCaveHeightRatio { get; init; }

    public required IFloatProvider StalactiteBluntness { get; init; }

    public required IFloatProvider StalagmiteBluntness { get; init; }

    public required IFloatProvider WindSpeed { get; init; }

    public required int MinRadiusForWind { get; init; }

    public required float MinBluntnessForWind { get; init; }

    private static IBlock DripstoneBlock => field ??= BlocksRegistry.Get(Material.DripstoneBlock);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !DripstoneUtils.IsEmptyOrWater(level.GetBlock(origin)))
            return false;

        var scanned = Column.Scan(level, origin, this.FloorToCeilingSearchRange, DripstoneUtils.IsEmptyOrWater,
            DripstoneUtils.IsDripstoneBaseOrLava);
        if (scanned is null || !scanned.Value.IsRange)
            return false;

        var column = scanned.Value;
        var caveHeight = column.Height!.Value;
        if (caveHeight < 4)
            return false;

        var maxRadius = (int)(caveHeight * this.MaxColumnRadiusToCaveHeightRatio);
        var clamped = Math.Clamp(maxRadius, this.ColumnRadius.MinValue, this.ColumnRadius.MaxValue);
        var radius = FeatureHelpers.RandomBetweenInclusive(random, this.ColumnRadius.MinValue, clamped);

        var stalactite = this.MakeDripstone(origin.AtY(column.Ceiling!.Value - 1), false, random, radius, this.StalactiteBluntness);
        var stalagmite = this.MakeDripstone(origin.AtY(column.Floor!.Value + 1), true, random, radius, this.StalagmiteBluntness);

        var wind = stalactite.IsSuitableForWind(this) && stalagmite.IsSuitableForWind(this)
            ? WindOffsetter.Create(origin.Y, random, this.WindSpeed)
            : WindOffsetter.None;

        var placeStalactite = stalactite.MoveBackUntilBaseIsInsideStoneAndShrinkRadiusIfNecessary(level, wind);
        var placeStalagmite = stalagmite.MoveBackUntilBaseIsInsideStoneAndShrinkRadiusIfNecessary(level, wind);

        if (placeStalactite)
            stalactite.PlaceBlocks(level, random, wind);

        if (placeStalagmite)
            stalagmite.PlaceBlocks(level, random, wind);

        return true;
    }

    private LargeDripstone MakeDripstone(Vector root, bool pointingUp, IRandomSource random, int radius, IFloatProvider bluntness)
    {
        // Bluntness is sampled before the height scale, like vanilla's argument order.
        var blunt = bluntness.Sample(random);
        var scale = this.HeightScale.Sample(random);
        return new LargeDripstone(root, pointingUp, radius, blunt, scale);
    }

    private sealed class LargeDripstone
    {
        private readonly bool pointingUp;
        private readonly double bluntness;
        private readonly double scale;
        private Vector root;
        private int radius;

        public LargeDripstone(Vector root, bool pointingUp, int radius, double bluntness, double scale)
        {
            this.root = root;
            this.pointingUp = pointingUp;
            this.radius = radius;
            this.bluntness = bluntness;
            this.scale = scale;
        }

        public bool IsSuitableForWind(LargeDripstoneFeature config) =>
            this.radius >= config.MinRadiusForWind && this.bluntness >= config.MinBluntnessForWind;

        public bool MoveBackUntilBaseIsInsideStoneAndShrinkRadiusIfNecessary(IWorldGenLevel level, WindOffsetter wind)
        {
            while (this.radius > 1)
            {
                var position = this.root;
                var attempts = Math.Min(10, this.GetHeightAtRadius(0.0f));

                for (var i = 0; i < attempts; i++)
                {
                    if (level.GetBlock(position).Material == Material.Lava)
                        return false;

                    if (DripstoneUtils.IsCircleMostlyEmbeddedInStone(level, wind.Offset(position), this.radius))
                    {
                        this.root = position;
                        return true;
                    }

                    position += this.pointingUp ? Vector.Down : Vector.Up;
                }

                this.radius /= 2;
            }

            return false;
        }

        public void PlaceBlocks(IWorldGenLevel level, IRandomSource random, WindOffsetter wind)
        {
            for (var dx = -this.radius; dx <= this.radius; dx++)
            {
                for (var dz = -this.radius; dz <= this.radius; dz++)
                {
                    var distance = (float)Math.Sqrt((float)(dx * dx + dz * dz));
                    if (distance > this.radius)
                        continue;

                    var height = this.GetHeightAtRadius(distance);
                    if (height <= 0)
                        continue;

                    if (random.NextFloat() < 0.2)
                        height = (int)(height * FeatureHelpers.RandomBetween(random, 0.8f, 1.0f));

                    var position = this.root + new Vector(dx, 0, dz);
                    var placedAny = false;
                    var maxY = this.pointingUp ? level.GetHeight(HeightmapType.WorldSurfaceWG, position.X, position.Z) : int.MaxValue;

                    for (var i = 0; i < height && position.Y < maxY; i++)
                    {
                        var target = wind.Offset(position);
                        if (DripstoneUtils.IsEmptyOrWaterOrLava(level.GetBlock(target)))
                        {
                            placedAny = true;
                            level.SetBlock(target, DripstoneBlock);
                        }
                        else if (placedAny && baseStoneOverworld.Contains(level.GetBlock(target)))
                        {
                            break;
                        }

                        position += this.pointingUp ? Vector.Up : Vector.Down;
                    }
                }
            }
        }

        private int GetHeightAtRadius(float distance) => (int)DripstoneUtils.GetDripstoneHeight(distance, this.radius, this.scale, this.bluntness);
    }

    /// <summary>
    /// Vanilla <c>WindOffsetter</c>: shifts each block horizontally in proportion to its distance from the origin's Y.
    /// </summary>
    private sealed class WindOffsetter
    {
        private readonly int originY;
        private readonly double windX;
        private readonly double windZ;
        private readonly bool active;

        private WindOffsetter(int originY, double windX, double windZ, bool active)
        {
            this.originY = originY;
            this.windX = windX;
            this.windZ = windZ;
            this.active = active;
        }

        public static WindOffsetter None { get; } = new(0, 0.0, 0.0, false);

        public static WindOffsetter Create(int originY, IRandomSource random, IFloatProvider windSpeed)
        {
            var speed = windSpeed.Sample(random);
            var angle = FeatureHelpers.RandomBetween(random, 0.0f, (float)Math.PI);
            return new WindOffsetter(originY, Mth.Cos(angle) * speed, Mth.Sin(angle) * speed, true);
        }

        public Vector Offset(Vector position)
        {
            if (!this.active)
                return position;

            var dy = this.originY - position.Y;
            return position + new Vector(Mth.Floor(this.windX * dy), 0, Mth.Floor(this.windZ * dy));
        }
    }
}
