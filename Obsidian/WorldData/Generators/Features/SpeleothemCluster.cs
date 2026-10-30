using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature SpeleothemCluster(JsonElement json)
    {
        var settings = new ClusterSettings(
            this.SpeleothemBlocks(json),
            json.GetProperty("floor_to_ceiling_search_range").GetInt32(),
            ValueProviders.Int(json.GetProperty("height")),
            ValueProviders.Int(json.GetProperty("radius")),
            json.GetProperty("max_stalagmite_stalactite_height_diff").GetInt32(),
            json.GetProperty("height_deviation").GetInt32(),
            ValueProviders.Int(json.GetProperty("speleothem_block_layer_thickness")),
            ValueProviders.Float(json.GetProperty("density")),
            ValueProviders.Float(json.GetProperty("wetness")),
            json.GetProperty("chance_of_speleothem_at_max_distance_from_center").GetSingle(),
            json.GetProperty("max_distance_from_edge_affecting_chance_of_speleothem").GetInt32(),
            json.GetProperty("max_distance_from_center_affecting_height_bias").GetInt32(),
            this.BlockTag("minecraft:base_stone_overworld"));

        return (context, random, origin) =>
        {
            var level = context.Level;

            if (!SpeleothemEmpty(level.GetBlock(origin)))
            {
                return false;
            }

            var height = settings.Height(random);
            var wetness = settings.Wetness(random);
            var density = settings.Density(random);
            var radiusX = settings.Radius(random);
            var radiusZ = settings.Radius(random);

            for (var dx = -radiusX; dx <= radiusX; dx++)
            {
                for (var dz = -radiusZ; dz <= radiusZ; dz++)
                {
                    var edge = Math.Min(radiusX - Math.Abs(dx), radiusZ - Math.Abs(dz));
                    var fraction = Math.Clamp((float)edge / settings.EdgeDistance, 0.0f, 1.0f);
                    var chance = (double)(settings.EdgeChance + fraction * (1.0f - settings.EdgeChance));
                    var position = origin + new Vector(dx, 0, dz);
                    PlaceClusterColumn(level, random, position, dx, dz, height, density, wetness, chance, settings);
                }
            }

            return true;
        };
    }

    private static void PlaceClusterColumn(
        IWorldGenLevel level,
        IRandomSource random,
        Vector position,
        int dx,
        int dz,
        int height,
        float density,
        float wetness,
        double chance,
        ClusterSettings settings)
    {
        var column = Column.Scan(
            level, position, settings.SearchRange, SpeleothemEmpty, state => !SpeleothemEmpty(state));

        if (column is null || column.Value.Floor is null && column.Value.Ceiling is null)
        {
            return;
        }

        var floor = column.Value.Floor;
        var ceiling = column.Value.Ceiling;
        var pool = random.NextFloat() < wetness;

        if (pool && floor is int poolY && ClusterPoolFits(level, position.AtY(poolY), settings))
        {
            level.SetBlock(position.AtY(poolY), BlocksRegistry.Water);
            floor = poolY - 1;
        }

        var down = 0;
        var growDown = random.NextDouble() < chance;

        if (ceiling is int ceilingY && growDown && !level.GetBlock(position.AtY(ceilingY)).Is(BlockIds.Lava))
        {
            var thickness = settings.Thickness(random);
            ClusterBase(level, position.AtY(ceilingY), BlockFace.Up, thickness, settings.Blocks);
            var limit = floor is int bottom ? Math.Min(height, ceilingY - bottom) : height;
            down = ClusterHeight(random, dx, dz, density, limit, settings);
        }

        var up = 0;
        var growUp = random.NextDouble() < chance;

        if (floor is int floorY && growUp && !level.GetBlock(position.AtY(floorY)).Is(BlockIds.Lava))
        {
            var thickness = settings.Thickness(random);
            ClusterBase(level, position.AtY(floorY), BlockFace.Down, thickness, settings.Blocks);
            if (ceiling is not null)
            {
                var difference = FeatureHelpers.RandomBetweenInclusive(
                    random, -settings.HeightDiff, settings.HeightDiff);
                up = Math.Max(0, down + difference);
            }
            else
            {
                up = ClusterHeight(random, dx, dz, density, height, settings);
            }
        }

        if (ceiling is int top && floor is int low && top - down <= low + up)
        {
            var min = Math.Max(top - down, low + 1);
            var max = Math.Min(low + up, top - 1);
            var meeting = FeatureHelpers.RandomBetweenInclusive(random, min, max + 1);
            down = top - meeting;
            up = meeting - 1 - low;
        }

        var gap = ceiling - floor - 1;
        var merge = random.NextBoolean() && down > 0 && up > 0 && gap == down + up;

        if (ceiling is int downY)
        {
            settings.Blocks.Grow(level, position.AtY(downY - 1), BlockFace.Down, down, merge);
        }

        if (floor is int upY)
        {
            settings.Blocks.Grow(level, position.AtY(upY + 1), BlockFace.Up, up, merge);
        }
    }

    private static int ClusterHeight(
        IRandomSource random, int dx, int dz, float density, int limit, ClusterSettings settings)
    {
        if (random.NextFloat() > density)
        {
            return 0;
        }

        var distance = Math.Abs(dx) + Math.Abs(dz);
        var fraction = Math.Clamp((double)distance / settings.BiasDistance, 0.0, 1.0);
        var half = limit / 2.0;
        var mean = (float)(half + fraction * -half);
        return (int)FeatureHelpers.ClampedNormal(random, mean, settings.Deviation, 0.0f, limit);
    }

    private static void ClusterBase(
        IWorldGenLevel level, Vector position, BlockFace direction, int thickness, SpeleothemStates blocks)
    {
        for (var i = 0; i < thickness; i++)
        {
            if (!blocks.PlaceBase(level, position))
            {
                return;
            }

            position = position.Offset(direction);
        }
    }

    private static bool ClusterPoolFits(IWorldGenLevel level, Vector position, ClusterSettings settings)
    {
        var state = level.GetBlock(position);

        if (state.Is(BlockIds.Water) || state.RegistryId == settings.Blocks.Base.RegistryId
            || state.RegistryId == settings.Blocks.Pointed.RegistryId
            || FeatureHelpers.IsWaterFluid(level.GetBlock(position + Vector.Up)))
        {
            return false;
        }

        BlockFace[] faces = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West, BlockFace.Down];

        return faces.All(face =>
        {
            var neighbor = level.GetBlock(position.Offset(face));
            return settings.PoolSupports.Contains(neighbor.RegistryId) || FeatureHelpers.IsWaterFluid(neighbor);
        });
    }

    private sealed record ClusterSettings(
        SpeleothemStates Blocks,
        int SearchRange,
        IntProvider Height,
        IntProvider Radius,
        int HeightDiff,
        int Deviation,
        IntProvider Thickness,
        FloatProvider Density,
        FloatProvider Wetness,
        float EdgeChance,
        int EdgeDistance,
        int BiasDistance,
        HashSet<int> PoolSupports);
}
