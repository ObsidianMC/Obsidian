using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature SteppedColumns(JsonElement json)
    {
        var settings = new SteppedSettings(
            this.Provider(json.GetProperty("block")),
            this.Predicate(json.GetProperty("continue_through")),
            this.Predicate(json.GetProperty("can_replace")),
            this.Blocks(json.GetProperty("cannot_place_on")),
            this.Int(json.GetProperty("column_reach")));
        var height = this.Int(json.GetProperty("height"));
        var clusterReach = this.Int(json.GetProperty("cluster_reach"));
        var columnCount = this.Int(json.GetProperty("column_count"));

        return (context, random, origin) =>
        {
            var level = context.Level;

            if (!SteppedBase(level, origin, settings))
            {
                return false;
            }

            var maxHeight = height(random);
            var reach = Math.Min(maxHeight, clusterReach(random));
            var count = columnCount(random);
            var placed = false;

            for (var i = 0; i < count; i++)
            {
                // randomBetweenClosed draws x, y (even its singleton range), then z, lazily per column.
                var x = random.NextInt(reach * 2 + 1) + origin.X - reach;
                var y = random.NextInt(1) + origin.Y;
                var z = random.NextInt(reach * 2 + 1) + origin.Z - reach;
                var center = new Vector(x, y, z);
                var columnHeight = maxHeight - FeatureHelpers.DistManhattan(center, origin);

                if (columnHeight >= 0)
                {
                    var columnReach = settings.Reach(random);
                    placed |= PlaceSteppedColumn(level, random, center, columnHeight, columnReach, settings);
                }
            }

            return placed;
        };
    }

    private static bool PlaceSteppedColumn(
        IWorldGenLevel level,
        IRandomSource random,
        Vector center,
        int height,
        int reach,
        SteppedSettings settings)
    {
        var placed = false;
        var extent = new Vector(reach, 0, reach);

        foreach (var column in FeatureHelpers.BetweenClosed(center - extent, center + extent))
        {
            var distance = FeatureHelpers.DistManhattan(center, column);
            var ground = SteppedSurface(level, column, distance, settings);

            if (ground is not Vector position)
            {
                continue;
            }

            var remaining = height - distance / 2;

            while (remaining >= 0)
            {
                if (settings.CanReplace(level, position))
                {
                    level.SetBlock(position, State(settings.Provider, level, random, position));
                    placed = true;
                }
                else if (!settings.Continue(level, position))
                {
                    break;
                }

                position += Vector.Up;
                remaining--;
            }
        }

        return placed;
    }

    private static Vector? SteppedSurface(
        IWorldGenLevel level, Vector position, int distance, SteppedSettings settings)
    {
        if (settings.CanReplace(level, position))
        {
            while (position.Y > level.MinY + 1 && distance-- > 0)
            {
                if (SteppedBase(level, position, settings))
                {
                    return position;
                }

                position += Vector.Down;
            }
        }
        else
        {
            while (position.Y < level.MinY + level.Height && distance-- > 0)
            {
                var state = level.GetBlock(position);

                if (settings.CannotPlaceOn.Contains(state.RegistryId))
                {
                    return null;
                }

                if (state.IsAir)
                {
                    return position;
                }

                position += Vector.Up;
            }
        }

        return null;
    }

    private static bool SteppedBase(IWorldGenLevel level, Vector position, SteppedSettings settings)
    {
        if (!settings.CanReplace(level, position))
        {
            return false;
        }

        var below = level.GetBlock(position + Vector.Down);
        return !below.IsAir && !settings.CannotPlaceOn.Contains(below.RegistryId);
    }

    private sealed record SteppedSettings(
        StateProvider Provider,
        BlockPredicate Continue,
        BlockPredicate CanReplace,
        HashSet<int> CannotPlaceOn,
        IntProvider Reach);
}
