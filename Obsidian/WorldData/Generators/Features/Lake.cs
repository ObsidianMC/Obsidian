using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature Lake(JsonElement json)
    {
        var fluidProvider = this.Provider(json.GetProperty("fluid"));
        var barrierProvider = this.Provider(json.GetProperty("barrier"));
        var canPlace = this.Predicate(json.GetProperty("can_place_feature"));
        var canFill = this.Predicate(json.GetProperty("can_replace_with_air_or_fluid"));
        var canLine = this.Predicate(json.GetProperty("can_replace_with_barrier"));

        return (context, random, origin) =>
        {
            var level = context.Level;

            if (origin.Y <= level.MinY + 4)
            {
                return false;
            }

            var corner = origin + new Vector(-8, -4, -8);
            var shape = LakeShape(random);
            var fluid = State(fluidProvider, level, random, corner);

            foreach (var offset in LakeVolume())
            {
                if (!LakeShell(shape, offset))
                {
                    continue;
                }

                var position = corner + offset;
                var existing = level.GetBlock(position);
                var liquid = existing.Is(BlockIds.Water) || existing.Is(BlockIds.Lava);
                var leaksAbove = offset.Y >= 4 && liquid;
                var leaksBelow = offset.Y < 4 && !existing.IsSolid() && !existing.IsSameState(fluid);

                if (leaksAbove || leaksBelow || !canPlace(level, position))
                {
                    return false;
                }
            }

            foreach (var offset in LakeVolume())
            {
                var position = corner + offset;

                if (!shape[LakeIndex(offset)] || !canFill(level, position))
                {
                    continue;
                }

                var aboveFluid = offset.Y >= 4;
                level.SetBlock(position, aboveFluid ? BlocksRegistry.CaveAir : fluid);

                if (aboveFluid)
                {
                    MarkAboveDisk(level, position);
                }
            }

            var barrier = State(barrierProvider, level, random, corner);

            if (!barrier.IsAir)
            {
                foreach (var offset in LakeVolume())
                {
                    if (!LakeShell(shape, offset) || offset.Y >= 4 && random.NextInt(2) == 0)
                    {
                        continue;
                    }

                    var position = corner + offset;

                    if (level.GetBlock(position).IsSolid() && canLine(level, position))
                    {
                        level.SetBlock(position, barrier);
                        MarkAboveDisk(level, position);
                    }
                }
            }

            if (FeatureHelpers.IsWaterFluid(fluid))
            {
                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                    {
                        var position = corner + new Vector(x, 4, z);
                        var biome = CodecRegistry.Biomes.All[context.BiomeAt(position)];

                        if (FreezeTopLayerFeature.ShouldFreeze(biome, level, position, false)
                            && canFill(level, position))
                        {
                            level.SetBlock(position, BlocksRegistry.Ice);
                        }
                    }
                }
            }

            return true;
        };
    }

    // The union of 4–7 ellipsoids. Coordinates and arithmetic remain double, matching 26.3 LakeFeature.
    private static bool[] LakeShape(IRandomSource random)
    {
        var shape = new bool[16 * 16 * 8];
        var count = random.NextInt(4) + 4;

        for (var i = 0; i < count; i++)
        {
            var width = random.NextDouble() * 6.0 + 3.0;
            var height = random.NextDouble() * 4.0 + 2.0;
            var depth = random.NextDouble() * 6.0 + 3.0;
            var centerX = random.NextDouble() * (16.0 - width - 2.0) + 1.0 + width / 2.0;
            var centerY = random.NextDouble() * (8.0 - height - 4.0) + 2.0 + height / 2.0;
            var centerZ = random.NextDouble() * (16.0 - depth - 2.0) + 1.0 + depth / 2.0;

            for (var x = 1; x < 15; x++)
            {
                for (var z = 1; z < 15; z++)
                {
                    for (var y = 1; y < 7; y++)
                    {
                        var dx = (x - centerX) / (width / 2.0);
                        var dy = (y - centerY) / (height / 2.0);
                        var dz = (z - centerZ) / (depth / 2.0);

                        if (dx * dx + dy * dy + dz * dz < 1.0)
                        {
                            shape[LakeIndex(new Vector(x, y, z))] = true;
                        }
                    }
                }
            }
        }

        return shape;
    }

    private static IEnumerable<Vector> LakeVolume()
    {
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var y = 0; y < 8; y++)
                {
                    yield return new Vector(x, y, z);
                }
            }
        }
    }

    private static int LakeIndex(Vector offset) => (offset.X * 16 + offset.Z) * 8 + offset.Y;

    private static bool LakeShell(bool[] shape, Vector offset)
    {
        if (shape[LakeIndex(offset)])
        {
            return false;
        }

        foreach (var face in FeatureHelpers.Directions)
        {
            var neighbor = offset.Offset(face);
            var inside = neighbor.X is >= 0 and < 16
                && neighbor.Z is >= 0 and < 16 && neighbor.Y is >= 0 and < 8;

            if (inside && shape[LakeIndex(neighbor)])
            {
                return true;
            }
        }

        return false;
    }
}
