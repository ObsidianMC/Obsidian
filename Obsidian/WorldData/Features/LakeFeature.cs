namespace Obsidian.WorldData.Features;

/// <summary>
/// A blob-shaped pool of <see cref="Fluid"/> with a <see cref="Barrier"/> rim, like vanilla's LakeFeature (lava lakes).
/// </summary>
/// <remarks>
/// The lake occupies a 16x8x16 box starting 4 blocks below the origin; the shape is the union of 4-7 random ellipsoids,
/// and the upper half becomes cave air.
/// </remarks>
[ConfiguredFeatureClass("minecraft:lake")]
public sealed class LakeFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:lake";

    public required IBlockStateProvider Fluid { get; init; }

    public required IBlockStateProvider Barrier { get; init; }

    private static readonly BlockSet featuresCannotReplace = new("#minecraft:features_cannot_replace");
    private static readonly BlockSet lavaPoolStoneCannotReplace = new("#minecraft:lava_pool_stone_cannot_replace");

    private static IBlock CaveAir => field ??= BlocksRegistry.Get(Material.CaveAir);

    private static IBlock Ice => field ??= BlocksRegistry.Get(Material.Ice);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || origin.Y <= level.MinY + 4)
            return false;

        origin += new Vector(0, -4, 0);

        var shape = new bool[2048];
        var blobs = random.NextInt(4) + 4;
        for (var i = 0; i < blobs; i++)
        {
            var sizeX = random.NextDouble() * 6.0 + 3.0;
            var sizeY = random.NextDouble() * 4.0 + 2.0;
            var sizeZ = random.NextDouble() * 6.0 + 3.0;
            var centerX = random.NextDouble() * (16.0 - sizeX - 2.0) + 1.0 + sizeX / 2.0;
            var centerY = random.NextDouble() * (8.0 - sizeY - 4.0) + 2.0 + sizeY / 2.0;
            var centerZ = random.NextDouble() * (16.0 - sizeZ - 2.0) + 1.0 + sizeZ / 2.0;

            for (var x = 1; x < 15; x++)
            {
                for (var z = 1; z < 15; z++)
                {
                    for (var y = 1; y < 7; y++)
                    {
                        var dx = (x - centerX) / (sizeX / 2.0);
                        var dy = (y - centerY) / (sizeY / 2.0);
                        var dz = (z - centerZ) / (sizeZ / 2.0);
                        if (dx * dx + dy * dy + dz * dz < 1.0)
                            shape[Index(x, y, z)] = true;
                    }
                }
            }
        }

        var fluid = this.Fluid.GetState(random, origin);

        // Reject lakes that would leak: liquid above the waterline or non-solid walls below it.
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var y = 0; y < 8; y++)
                {
                    if (!IsShell(shape, x, y, z))
                        continue;

                    var existing = level.GetBlock(origin + new Vector(x, y, z));
                    if (y >= 4 && existing.IsLiquid)
                        return false;

                    if (y < 4 && !existing.IsSolid() && !existing.IsSameState(fluid))
                        return false;
                }
            }
        }

        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var y = 0; y < 8; y++)
                {
                    if (!shape[Index(x, y, z)])
                        continue;

                    var position = origin + new Vector(x, y, z);
                    if (!featuresCannotReplace.Contains(level.GetBlock(position)))
                        level.SetBlock(position, y >= 4 ? CaveAir : fluid);
                }
            }
        }

        var barrier = this.Barrier.GetState(random, origin);
        if (!barrier.IsAir)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                {
                    for (var y = 0; y < 8; y++)
                    {
                        // Above the waterline the rim is only rolled with a coin flip.
                        if (!IsShell(shape, x, y, z) || y >= 4 && random.NextInt(2) == 0)
                            continue;

                        var position = origin + new Vector(x, y, z);
                        var existing = level.GetBlock(position);
                        if (existing.IsSolid() && !lavaPoolStoneCannotReplace.Contains(existing))
                            level.SetBlock(position, barrier);
                    }
                }
            }
        }

        if (FeatureHelpers.IsWaterFluid(fluid))
        {
            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                {
                    var position = origin + new Vector(x, 4, z);
                    if (FreezeTopLayerFeature.ShouldFreeze(level.GetBiome(position), level, position, false)
                        && !featuresCannotReplace.Contains(level.GetBlock(position)))
                    {
                        level.SetBlock(position, Ice);
                    }
                }
            }
        }

        return true;
    }

    private static int Index(int x, int y, int z) => (x * 16 + z) * 8 + y;

    // Outside the shape but touching it on one of the 6 sides.
    private static bool IsShell(bool[] shape, int x, int y, int z) =>
        !shape[Index(x, y, z)]
        && (x < 15 && shape[Index(x + 1, y, z)]
            || x > 0 && shape[Index(x - 1, y, z)]
            || z < 15 && shape[Index(x, y, z + 1)]
            || z > 0 && shape[Index(x, y, z - 1)]
            || y < 7 && shape[Index(x, y + 1, z)]
            || y > 0 && shape[Index(x, y - 1, z)]);
}
