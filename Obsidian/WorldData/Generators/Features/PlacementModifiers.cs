using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Density;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>Adds the positions a modifier makes of one position to <c>output</c>, in vanilla's order.</summary>
internal delegate void PlacementModifier(
    PlacementContext context,
    IRandomSource random,
    Vector position,
    List<Vector> output);

/// <summary>A placement's decoration context and the top-level placed feature the biome modifier checks.</summary>
internal sealed record PlacementContext(DecorationContext Decoration, PlacedFeature? TopFeature)
{
    public int GetHeight(HeightmapType type, int x, int z) => this.Decoration.Level.GetHeight(type, x, z);
}

internal sealed partial class FeatureRegistry
{
    // Biome.BIOME_INFO_NOISE in 26.3: zero-offset simplex, rounded to float before count arithmetic.
    private static readonly SimplexNoise placementNoise = new(new WorldgenRandom(new LegacyRandomSource(2345L)));

    public PlacementModifier Modifier(JsonElement json)
    {
        var type = json.GetProperty("type").GetString();

        switch (type)
        {
            case "minecraft:count":
                return Repeat(ValueProviders.Int(json.GetProperty("count")));
            case "minecraft:in_square":
                return static (_, random, position, output) =>
                {
                    var x = random.NextInt(16) + position.X;
                    var z = random.NextInt(16) + position.Z;
                    output.Add(new Vector(x, position.Y, z));
                };
            case "minecraft:heightmap":
                return Heightmap(json);
            case "minecraft:biome":
                return this.Biome;
            case "minecraft:block_predicate_filter":
            {
                var predicate = this.Predicate(json.GetProperty("predicate"));
                return Filter((context, _, position) => predicate(context.Decoration.Level, position));
            }
            case "minecraft:rarity_filter":
            {
                var chance = json.GetProperty("chance").GetInt32();
                return Filter((_, random, _) => random.NextFloat() < 1.0f / chance);
            }
            case "minecraft:random_chance":
            {
                var probability = json.GetProperty("chance").GetSingle();
                return Filter((_, random, _) => random.NextFloat() < probability);
            }
            case "minecraft:height_range":
                return HeightRange(json);
            case "minecraft:surface_water_depth_filter":
            {
                var maxDepth = json.GetProperty("max_water_depth").GetInt32();
                return Filter((context, _, position) =>
                {
                    var oceanFloor = context.GetHeight(HeightmapType.OceanFloor, position.X, position.Z);
                    var surface = context.GetHeight(HeightmapType.WorldSurface, position.X, position.Z);

                    return surface - oceanFloor <= maxDepth;
                });
            }
            case "minecraft:surface_relative_threshold_filter":
                return SurfaceRelativeThreshold(json);
            case "minecraft:offset":
            {
                var x = ValueProviders.Int(json.GetProperty("x"));
                var y = ValueProviders.Int(json.GetProperty("y"));
                var z = ValueProviders.Int(json.GetProperty("z"));
                return (_, random, position, output) =>
                {
                    // Java evaluates the offset's arguments in order: x, y, then z.
                    var dx = x(random);
                    var dy = y(random);
                    var dz = z(random);
                    output.Add(position + new Vector(dx, dy, dz));
                };
            }
            case "minecraft:noise_threshold_count":
                return NoiseThresholdCount(json);
            case "minecraft:noise_based_count":
                return NoiseBasedCount(json);
            case "minecraft:environment_scan":
                return this.EnvironmentScan(json);
            case "minecraft:fixed_placement":
                return FixedPlacement(json);
            case "minecraft:count_on_every_layer":
                return CountOnEveryLayer(json);
            case "minecraft:randomly_selected":
                return this.RandomlySelectedPlacement(json);
            case "minecraft:cuboid":
                return CuboidPlacement(json);
            default:
                throw new NotSupportedException($"The 26.3 placement modifier {type} isn't implemented yet.");
        }
    }

    private static PlacementModifier Filter(Func<PlacementContext, IRandomSource, Vector, bool> test) =>
        (context, random, position, output) =>
        {
            if (test(context, random, position))
            {
                output.Add(position);
            }
        };

    private static PlacementModifier Repeat(IntProvider count) =>
        (_, random, position, output) => AddRepeated(output, position, count(random));

    private static void AddRepeated(List<Vector> output, Vector position, int count)
    {
        for (var i = 0; i < count; i++)
        {
            output.Add(position);
        }
    }

    private static PlacementModifier Heightmap(JsonElement json)
    {
        var type = HeightmapTypes.Parse(json.GetProperty("heightmap").GetString()!);

        return (context, _, position, output) =>
        {
            var y = context.GetHeight(type, position.X, position.Z);

            if (y > context.Decoration.Level.MinY)
            {
                output.Add(new Vector(position.X, y, position.Z));
            }
        };
    }

    private void Biome(PlacementContext context, IRandomSource random, Vector position, List<Vector> output)
    {
        var top = context.TopFeature
            ?? throw new InvalidOperationException("A biome filter needs the placed feature a biome decorates with.");
        var biome = context.Decoration.BiomeAt(position);

        if (this.BiomeHasFeature(biome, top))
        {
            output.Add(position);
        }
    }

    private static PlacementModifier HeightRange(JsonElement json)
    {
        var height = ValueProviders.Height(json.GetProperty("height"));

        return (context, random, position, output) =>
        {
            var decoration = context.Decoration;
            var y = height(random, decoration.GeneratorMinY, decoration.GeneratorHeight);
            output.Add(new Vector(position.X, y, position.Z));
        };
    }

    // The heightmap's height plus the bounds, in long like vanilla's, so absent bounds can't overflow.
    private static PlacementModifier SurfaceRelativeThreshold(JsonElement json)
    {
        var type = HeightmapTypes.Parse(json.GetProperty("heightmap").GetString()!);
        var minInclusive = json.TryGetProperty("min_inclusive", out var min) ? min.GetInt32() : int.MinValue;
        var maxInclusive = json.TryGetProperty("max_inclusive", out var max) ? max.GetInt32() : int.MaxValue;

        return Filter((context, _, position) =>
        {
            long surface = context.GetHeight(type, position.X, position.Z);
            var low = surface + minInclusive;
            var high = surface + maxInclusive;

            return low <= position.Y && position.Y <= high;
        });
    }

    private static PlacementModifier NoiseThresholdCount(JsonElement json)
    {
        var level = json.GetProperty("noise_level").GetDouble();
        var below = json.GetProperty("below_noise").GetInt32();
        var above = json.GetProperty("above_noise").GetInt32();

        return (_, _, position, output) =>
        {
            var noise = placementNoise.Get(position.X / 200.0, position.Z / 200.0);
            var count = noise < level ? below : above;
            AddRepeated(output, position, count);
        };
    }

    private static PlacementModifier NoiseBasedCount(JsonElement json)
    {
        var ratio = json.GetProperty("noise_to_count_ratio").GetInt32();
        var factor = json.GetProperty("noise_factor").GetDouble();
        var offset = json.TryGetProperty("noise_offset", out var offsetJson) ? offsetJson.GetDouble() : 0.0;

        return (_, _, position, output) =>
        {
            var noise = placementNoise.Get(position.X / factor, position.Z / factor);
            var count = (int)Math.Ceiling((noise + offset) * ratio);
            AddRepeated(output, position, count);
        };
    }

    // Steps from the position while the allowed condition holds, until the target condition does.
    private PlacementModifier EnvironmentScan(JsonElement json)
    {
        var direction = FeatureHelpers.ParseFace(json.GetProperty("direction_of_search").GetString());
        var step = direction == BlockFace.Up ? Vector.Up : Vector.Down;
        var target = this.Predicate(json.GetProperty("target_condition"));
        var maxSteps = json.GetProperty("max_steps").GetInt32();
        BlockPredicate allowed = static (_, _) => true;

        if (json.TryGetProperty("allowed_search_condition", out var allowedJson))
        {
            allowed = this.Predicate(allowedJson);
        }

        return (context, _, position, output) =>
        {
            var level = context.Decoration.Level;
            var current = position;

            if (!allowed(level, current))
            {
                return;
            }

            for (var i = 0; i < maxSteps; i++)
            {
                if (target(level, current))
                {
                    output.Add(current);
                    return;
                }

                current += step;

                if (level.IsOutsideBuildHeight(current.Y))
                {
                    return;
                }

                if (!allowed(level, current))
                {
                    break;
                }
            }

            if (target(level, current))
            {
                output.Add(current);
            }
        };
    }
}

internal static class HeightmapTypes
{
    public static HeightmapType Parse(string name) => name switch
    {
        "WORLD_SURFACE_WG" => HeightmapType.WorldSurfaceWG,
        "WORLD_SURFACE" => HeightmapType.WorldSurface,
        "OCEAN_FLOOR_WG" => HeightmapType.OceanFloorWG,
        "OCEAN_FLOOR" => HeightmapType.OceanFloor,
        "MOTION_BLOCKING" => HeightmapType.MotionBlocking,
        "MOTION_BLOCKING_NO_LEAVES" => HeightmapType.MotionBlockingNoLeaves,
        _ => throw new InvalidDataException($"Unknown heightmap {name}.")
    };
}
