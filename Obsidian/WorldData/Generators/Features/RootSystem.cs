using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature RootSystem(JsonElement json)
    {
        var settings = new RootSettings(
            this.Placed(json.GetProperty("feature")),
            this.Predicate(json.GetProperty("allowed_tree_position")),
            json.GetProperty("required_vertical_space_for_tree").GetInt32(),
            json.GetProperty("allowed_vertical_water_for_tree").GetInt32(),
            json.GetProperty("level_test_distance").GetInt32(),
            json.GetProperty("max_level_deviation").GetInt32(),
            json.GetProperty("root_radius").GetInt32(),
            this.Blocks(json.GetProperty("root_replaceable")),
            this.Provider(json.GetProperty("root_state_provider")),
            json.GetProperty("root_placement_attempts").GetInt32(),
            json.GetProperty("root_column_max_height").GetInt32(),
            json.GetProperty("hanging_root_radius").GetInt32(),
            json.GetProperty("hanging_roots_vertical_span").GetInt32(),
            this.Provider(json.GetProperty("hanging_root_state_provider")),
            json.GetProperty("hanging_root_placement_attempts").GetInt32());

        return (context, random, origin) =>
        {
            var level = context.Level;

            if (!level.GetBlock(origin).IsAir)
            {
                return false;
            }

            for (var i = 0; i < settings.MaxHeight; i++)
            {
                var position = origin + new Vector(0, i + 1, 0);
                var surface = level.GetHeight(HeightmapType.WorldSurface, position.X, position.Z);

                if (surface < position.Y)
                {
                    break;
                }

                if (!settings.Allowed(level, position) || !RootSpace(level, position, settings))
                {
                    continue;
                }

                var below = level.GetBlock(position + Vector.Down);

                if (FeatureHelpers.IsLavaFluid(below) || !below.IsSolid())
                {
                    break;
                }

                if (!settings.Child.Place(context, random, position))
                {
                    continue;
                }

                RootColumn(level, random, origin, origin.Y + i, settings);
                this.HangingRoots(level, random, origin, settings);
                break;
            }

            return true;
        };
    }

    private static bool RootSpace(IWorldGenLevel level, Vector position, RootSettings settings)
    {
        for (var i = 1; i <= settings.Space; i++)
        {
            var state = level.GetBlock(position + new Vector(0, i, 0));
            var allowedWater = i + 1 <= settings.WaterSpace && FeatureHelpers.IsWaterFluid(state);

            if (!state.IsAir && !allowedWater)
            {
                return false;
            }
        }

        if (settings.LevelDistance > 0)
        {
            foreach (var face in FeatureHelpers.Horizontal)
            {
                var sample = position.Offset(face, settings.LevelDistance);
                var below = level.GetBlock(sample + new Vector(0, -settings.Deviation, 0));
                var above = level.GetBlock(sample + new Vector(0, settings.Deviation, 0));

                if (below.IsAir || !above.IsAir)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void RootColumn(
        IWorldGenLevel level, IRandomSource random, Vector origin, int top, RootSettings settings)
    {
        for (var y = origin.Y; y < top; y++)
        {
            for (var attempt = 0; attempt < settings.Attempts; attempt++)
            {
                var dx = random.NextInt(settings.Radius) - random.NextInt(settings.Radius);
                var dz = random.NextInt(settings.Radius) - random.NextInt(settings.Radius);
                var position = new Vector(origin.X + dx, y, origin.Z + dz);

                if (settings.Replaceable.Contains(level.GetBlock(position).RegistryId))
                {
                    level.SetBlock(position, State(settings.Root, level, random, position));
                }
            }
        }
    }

    private void HangingRoots(IWorldGenLevel level, IRandomSource random, Vector origin, RootSettings settings)
    {
        for (var i = 0; i < settings.HangingAttempts; i++)
        {
            var dx = random.NextInt(settings.HangingRadius) - random.NextInt(settings.HangingRadius);
            var dy = random.NextInt(settings.HangingSpan) - random.NextInt(settings.HangingSpan);
            var dz = random.NextInt(settings.HangingRadius) - random.NextInt(settings.HangingRadius);
            var position = origin + new Vector(dx, dy, dz);

            if (!level.GetBlock(position).IsAir)
            {
                continue;
            }

            var state = State(settings.HangingRoot, level, random, position);

            if (this.CanSurvive(state, level, position)
                && level.GetBlock(position + Vector.Up).IsFaceSturdy(BlockFace.Down))
            {
                level.SetBlock(position, state);
            }
        }
    }

    private sealed record RootSettings(
        PlacedFeature Child,
        BlockPredicate Allowed,
        int Space,
        int WaterSpace,
        int LevelDistance,
        int Deviation,
        int Radius,
        HashSet<int> Replaceable,
        StateProvider Root,
        int Attempts,
        int MaxHeight,
        int HangingRadius,
        int HangingSpan,
        StateProvider HangingRoot,
        int HangingAttempts);
}
