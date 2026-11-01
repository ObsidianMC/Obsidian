using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

// 26.1 replaced the flower, random-patch, coral, kelp and seagrass feature types with these general features plus
// placement: a patch is now a simple_block (or block_column) placed with count, offset and a block predicate filter.
internal sealed partial class FeatureRegistry
{
    // Places the provider's state if it can survive. Double plants also need a free (or same-fluid, replaceable) block
    // above, and place both halves. Vanilla's schedule_tick block tick isn't scheduled: the region has no block ticks.
    private Feature SimpleBlock(JsonElement json)
    {
        var provider = this.Provider(json.GetProperty("to_place"));

        return (context, random, origin) =>
        {
            var level = context.Level;
            var state = provider(level, random, origin);

            if (state is null || !this.CanSurvive(state, level, origin))
            {
                return false;
            }

            if (IsDoublePlant(state))
            {
                var above = level.GetBlock(origin + Vector.Up);
                var fits = above.IsAir || (state.GetFluid() == above.GetFluid() && above.CanBeReplaced());

                if (!fits)
                {
                    return false;
                }

                SimpleBlockFeature.PlaceDoublePlant(level, state, origin);
            }
            else if (state.BlockClass() == "MossyCarpetBlock")
            {
                PlaceMossCarpet(level, origin);
            }
            else
            {
                level.SetBlock(origin, state);
            }

            return true;
        };
    }

    // The block classes extending vanilla's DoublePlantBlock.
    private static bool IsDoublePlant(IBlock state) =>
        state.BlockClass() is "DoublePlantBlock" or "TallFlowerBlock" or "TallSeagrassBlock" or "SmallDripleafBlock";

    // The first entry whose chance a draw falls under, otherwise the default.
    private Feature RandomSelector(JsonElement json)
    {
        var entries = new List<(float Chance, PlacedFeature Feature)>();

        foreach (var entry in json.GetProperty("features").EnumerateArray())
        {
            entries.Add((entry.GetProperty("chance").GetSingle(), this.Placed(entry.GetProperty("feature"))));
        }

        var fallback = this.Placed(json.GetProperty("default"));

        return (context, random, origin) =>
        {
            foreach (var (chance, feature) in entries)
            {
                if (random.NextFloat() < chance)
                {
                    return feature.Place(context, random, origin);
                }
            }

            return fallback.Place(context, random, origin);
        };
    }

    private Feature SimpleRandomSelector(JsonElement json)
    {
        var features = json.GetProperty("features").EnumerateArray().Select(this.Placed).ToArray();

        return (context, random, origin) =>
        {
            var feature = features[random.NextInt(features.Length)];
            return feature.Place(context, random, origin);
        };
    }

    private Feature RandomBooleanSelector(JsonElement json)
    {
        var whenTrue = this.Placed(json.GetProperty("feature_true"));
        var whenFalse = this.Placed(json.GetProperty("feature_false"));

        return (context, random, origin) =>
        {
            var feature = random.NextBoolean() ? whenTrue : whenFalse;
            return feature.Place(context, random, origin);
        };
    }

    private Feature WeightedRandomSelector(JsonElement json)
    {
        var entries = ValueProviders.Weighted(json.GetProperty("features"), this.Placed);

        return (context, random, origin) =>
        {
            var feature = entries.Pick(random);
            return feature is not null && feature.Place(context, random, origin);
        };
    }

    // Places the features in order, stopping at the first that places nothing.
    private Feature Sequence(JsonElement json)
    {
        var features = json.GetProperty("features").EnumerateArray().Select(this.Placed).ToArray();

        return (context, random, origin) =>
        {
            foreach (var feature in features)
            {
                if (!feature.Place(context, random, origin))
                {
                    return false;
                }
            }

            return true;
        };
    }

    // Samples every layer's height, shortens the column to the free space (from the tip or the base), then places
    // the layers from the origin along the direction.
    private Feature BlockColumn(JsonElement json)
    {
        var direction = FeatureHelpers.ParseFace(json.GetProperty("direction").GetString());
        var allowed = this.Predicate(json.GetProperty("allowed_placement"));
        var prioritizeTip = json.GetProperty("prioritize_tip").GetBoolean();
        var layers = new List<(IntProvider Height, StateProvider Provider)>();

        foreach (var layer in json.GetProperty("layers").EnumerateArray())
        {
            layers.Add((this.Int(layer.GetProperty("height")), this.Provider(layer.GetProperty("provider"))));
        }

        return (context, random, origin) =>
        {
            var level = context.Level;
            var heights = layers.Select(layer => layer.Height(random)).ToArray();
            var total = heights.Sum();

            if (total == 0)
            {
                return false;
            }

            var probe = origin.Offset(direction);

            for (var i = 0; i < total; i++)
            {
                if (!allowed(level, probe))
                {
                    TruncateColumn(heights, total, i, prioritizeTip);
                    break;
                }

                probe = probe.Offset(direction);
            }

            var position = origin;

            for (var i = 0; i < heights.Length; i++)
            {
                for (var j = 0; j < heights[i]; j++)
                {
                    var state = State(layers[i].Provider, level, random, position);
                    level.SetBlock(position, state);
                    position = position.Offset(direction);
                }
            }

            return true;
        };
    }

    private static void TruncateColumn(int[] heights, int total, int allowed, bool prioritizeTip)
    {
        var toRemove = total - allowed;
        var step = prioritizeTip ? 1 : -1;
        var start = prioritizeTip ? 0 : heights.Length - 1;
        var end = prioritizeTip ? heights.Length : -1;

        for (var i = start; i != end && toRemove > 0; i += step)
        {
            var removed = Math.Min(heights[i], toRemove);
            toRemove -= removed;
            heights[i] -= removed;
        }
    }

    // Replaces the target blocks of a vertical cylinder: z-major, x-minor columns from the top down.
    private Feature Disk(JsonElement json)
    {
        var provider = this.Provider(json.GetProperty("state_provider"));
        var target = this.Predicate(json.GetProperty("target"));
        var radiusProvider = this.Int(json.GetProperty("radius"));
        var halfHeight = json.GetProperty("half_height").GetInt32();

        return (context, random, origin) =>
        {
            var level = context.Level;
            var top = origin.Y + halfHeight;
            var bottom = origin.Y - halfHeight - 1;
            var radius = radiusProvider(random);
            var placed = false;

            for (var z = origin.Z - radius; z <= origin.Z + radius; z++)
            {
                for (var x = origin.X - radius; x <= origin.X + radius; x++)
                {
                    var dx = x - origin.X;
                    var dz = z - origin.Z;

                    if (dx * dx + dz * dz > radius * radius)
                    {
                        continue;
                    }

                    var replacingSegment = false;

                    for (var y = top; y > bottom; y--)
                    {
                        var position = new Vector(x, y, z);

                        if (!target(level, position))
                        {
                            replacingSegment = false;
                            continue;
                        }

                        var state = provider(level, random, position);

                        if (state is not null)
                        {
                            level.SetBlock(position, state);

                            // Mark only at the start of each matching segment, as 26.3 DiskFeature does.
                            if (!replacingSegment)
                            {
                                FeatureHelpers.MarkAboveForPostProcessing(level, position);
                            }

                            replacingSegment = true;
                            placed = true;
                        }
                    }
                }
            }

            return placed;
        };
    }

    // Moves down to a block it can sit on, then places three small, randomly sized and drifting balls.
    private Feature BlockBlob(JsonElement json)
    {
        var canPlaceOn = this.Predicate(json.GetProperty("can_place_on"));
        var state = ReadBlock(json.GetProperty("state"));

        return (context, random, origin) =>
        {
            var level = context.Level;
            var floor = level.MinY + 3;

            while (origin.Y > floor && !canPlaceOn(level, origin + Vector.Down))
            {
                origin += Vector.Down;
            }

            if (origin.Y <= floor)
            {
                return false;
            }

            for (var i = 0; i < 3; i++)
            {
                var sizeX = random.NextInt(2);
                var sizeY = random.NextInt(2);
                var sizeZ = random.NextInt(2);
                var radius = (sizeX + sizeY + sizeZ) * 0.333f + 0.5f;
                var size = new Vector(sizeX, sizeY, sizeZ);
                var limit = (double)(radius * radius);

                foreach (var position in FeatureHelpers.BetweenClosed(origin - size, origin + size))
                {
                    if (FeatureHelpers.DistSqr(position, origin) <= limit)
                    {
                        level.SetBlock(position, state);
                    }
                }

                var dx = -1 + random.NextInt(2);
                var dy = -random.NextInt(2);
                var dz = -1 + random.NextInt(2);
                origin += new Vector(dx, dy, dz);
            }

            return true;
        };
    }

    // A fluid source in a wall: valid blocks above (and below), and exactly the configured rock and air neighbours
    // among the four sides and below.
    private Feature Spring(JsonElement json)
    {
        var fluid = LiquidBlock(json.GetProperty("state"));
        var validBlocks = this.Blocks(json.GetProperty("valid_blocks"));
        var requiresBelow = !json.TryGetProperty("requires_block_below", out var below) || below.GetBoolean();
        var rockCount = json.TryGetProperty("rock_count", out var rocksJson) ? rocksJson.GetInt32() : 4;
        var holeCount = json.TryGetProperty("hole_count", out var holesJson) ? holesJson.GetInt32() : 1;
        Vector[] neighbours = [Vector.West, Vector.East, Vector.North, Vector.South, Vector.Down];

        return (context, _, origin) =>
        {
            var level = context.Level;

            if (!validBlocks.Contains(level.GetBlock(origin + Vector.Up).RegistryId))
            {
                return false;
            }

            if (requiresBelow && !validBlocks.Contains(level.GetBlock(origin + Vector.Down).RegistryId))
            {
                return false;
            }

            var current = level.GetBlock(origin);

            if (!current.IsAir && !validBlocks.Contains(current.RegistryId))
            {
                return false;
            }

            var rocks = neighbours.Count(offset => validBlocks.Contains(level.GetBlock(origin + offset).RegistryId));
            var holes = neighbours.Count(offset => level.GetBlock(origin + offset).IsAir);

            if (rocks != rockCount || holes != holeCount)
            {
                return false;
            }

            level.SetBlock(origin, fluid);
            level.ScheduleFluidTick(origin);
            return true;
        };
    }

    // FluidState.createLegacyBlock: a source is level 0 (falling or not); flowing fluids count down from 8.
    private static IBlock LiquidBlock(JsonElement json)
    {
        const string flowingPrefix = "minecraft:flowing_";
        var id = WorldgenData.Qualify(json.GetProperty("id").GetString()!);
        var properties = json.TryGetProperty("properties", out var values) ? values : default;
        var flowing = id.StartsWith(flowingPrefix, StringComparison.Ordinal);
        var block = BlocksRegistry.Get(flowing ? "minecraft:" + id[flowingPrefix.Length..] : id);

        if (!flowing)
        {
            return block.WithProperty("level", "0");
        }

        var amount = int.Parse(properties.GetProperty("level").GetString()!);
        var falling = properties.TryGetProperty("falling", out var fallingJson) && fallingJson.GetString() == "true";
        var level = 8 - Math.Min(amount, 8) + (falling ? 8 : 0);

        return block.WithProperty("level", level.ToString());
    }
}
