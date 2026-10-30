using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature Geode(JsonElement json)
    {
        var blocks = json.GetProperty("blocks");
        var layers = json.GetProperty("layers");
        var crack = json.GetProperty("crack");
        var filling = this.Provider(blocks.GetProperty("filling_provider"));
        var inner = this.Provider(blocks.GetProperty("inner_layer_provider"));
        var alternate = this.Provider(blocks.GetProperty("alternate_inner_layer_provider"));
        var middle = this.Provider(blocks.GetProperty("middle_layer_provider"));
        var outer = this.Provider(blocks.GetProperty("outer_layer_provider"));
        var placements = blocks.GetProperty("inner_placements").EnumerateArray().Select(ReadBlock).ToArray();
        var cannotReplace = this.Blocks(blocks.GetProperty("cannot_replace"));
        var invalidBlocks = this.Blocks(blocks.GetProperty("invalid_blocks"));
        var invalidThreshold = json.GetProperty("invalid_blocks_threshold").GetInt32();
        var distanceJson = json.TryGetProperty("outer_wall_distance", out var distance)
            ? distance : JsonSerializer.SerializeToElement(new
            {
                type = "minecraft:uniform",
                min_inclusive = 4,
                max_inclusive = 5
            });
        var wallDistance = ValueProviders.Int(distanceJson);
        var reader = new WorldgenReader(
            WorldgenTypes.BuiltIn,
            _ => throw new InvalidOperationException("Geodes cannot resolve configured features."),
            _ => throw new InvalidOperationException("Geodes cannot resolve placed features."));
        var maxWallDistance = reader.Read<IIntProvider>(distanceJson).MaxValue;
        var pointCount = json.TryGetProperty("distribution_points", out var points)
            ? ValueProviders.Int(points) : random => random.NextIntBetweenInclusive(3, 4);
        var pointOffset = json.TryGetProperty("point_offset", out var offset)
            ? ValueProviders.Int(offset) : random => random.NextIntBetweenInclusive(1, 2);
        var min = json.TryGetProperty("min_gen_offset", out var minJson) ? minJson.GetInt32() : -16;
        var max = json.TryGetProperty("max_gen_offset", out var maxJson) ? maxJson.GetInt32() : 16;
        var noiseMultiplier = DoubleOr(json, "noise_multiplier", 0.05);
        var alternateChance = DoubleOr(json, "use_alternate_layer0_chance", 0.0);
        var placementChance = DoubleOr(json, "use_potential_placements_chance", 0.35);
        var requireAlternate = !json.TryGetProperty("placements_require_layer0_alternate", out var required)
            || required.GetBoolean();
        var fillingSize = DoubleOr(layers, "filling", 1.7);
        var innerSize = DoubleOr(layers, "inner_layer", 2.2);
        var middleSize = DoubleOr(layers, "middle_layer", 3.2);
        var outerSize = DoubleOr(layers, "outer_layer", 4.2);
        var crackChance = DoubleOr(crack, "generate_crack_chance", 1.0);
        var crackBase = DoubleOr(crack, "base_crack_size", 2.0);
        var crackOffset = crack.TryGetProperty("crack_point_offset", out var crackPoint)
            ? crackPoint.GetInt32() : 2;
        var noiseParameters = new NoiseParameters(-4, 1.0, 1, NoiseNormalization.Legacy, [1.0]);

        return (context, random, origin) =>
        {
            var level = context.Level;
            var count = pointCount(random);
            var noise = noiseParameters.Create(new WorldgenRandom(new LegacyRandomSource(level.Seed)));
            var scale = (double)count / maxWallDistance;
            var fillingThreshold = 1.0 / Math.Sqrt(fillingSize);
            var innerThreshold = 1.0 / Math.Sqrt(innerSize + scale);
            var middleThreshold = 1.0 / Math.Sqrt(middleSize + scale);
            var outerThreshold = 1.0 / Math.Sqrt(outerSize + scale);
            var crackThreshold = 1.0 / Math.Sqrt(
                crackBase + random.NextDouble() / 2.0 + (count > 3 ? scale : 0.0));
            var makeCrack = random.NextFloat() < crackChance;
            var centers = new List<(Vector Position, int Offset)>();
            var invalid = 0;

            for (var i = 0; i < count; i++)
            {
                var x = wallDistance(random);
                var y = wallDistance(random);
                var z = wallDistance(random);
                var position = origin + new Vector(x, y, z);
                var state = level.GetBlock(position);

                if ((state.IsAir || invalidBlocks.Contains(state.RegistryId)) && ++invalid > invalidThreshold)
                {
                    return false;
                }

                centers.Add((position, pointOffset(random)));
            }

            var cracks = new List<Vector>();

            if (makeCrack)
            {
                var side = random.NextInt(4);
                var reach = count * 2 + 1;
                var x = side is 0 or 2 ? reach : 0;
                var z = side is 1 or 2 ? reach : 0;
                cracks.Add(origin + new Vector(x, 7, z));
                cracks.Add(origin + new Vector(x, 5, z));
                cracks.Add(origin + new Vector(x, 1, z));
            }

            var buds = new List<Vector>();
            Func<IBlock, bool> replace = state => !cannotReplace.Contains(state.RegistryId);

            foreach (var position in FeatureHelpers.BetweenClosed(origin + new Vector(min), origin + new Vector(max)))
            {
                // 26.3 noise sums in float; shell and crack distances stay double.
                var perturbation = noise.Get(position.X, position.Y, position.Z) * noiseMultiplier;
                var shell = 0.0;

                foreach (var center in centers)
                {
                    shell += 1.0 / Math.Sqrt(FeatureHelpers.DistSqr(position, center.Position) + center.Offset)
                        + perturbation;
                }

                if (shell < outerThreshold)
                {
                    continue;
                }

                if (shell >= fillingThreshold)
                {
                    FeatureHelpers.SafeSetBlock(level, position, State(filling, level, random, position), replace);
                    continue;
                }

                var crackValue = 0.0;

                foreach (var center in cracks)
                {
                    crackValue += 1.0 / Math.Sqrt(FeatureHelpers.DistSqr(position, center) + crackOffset)
                        + perturbation;
                }

                if (makeCrack && crackValue >= crackThreshold)
                {
                    FeatureHelpers.SafeSetBlock(level, position, BlocksRegistry.Air, replace);

                    foreach (var face in FeatureHelpers.Directions)
                    {
                        var neighbor = position.Offset(face);

                        if (level.GetBlock(neighbor).HasFluid())
                        {
                            level.ScheduleFluidTick(neighbor);
                        }
                    }
                }
                else if (shell >= innerThreshold)
                {
                    var useAlternate = random.NextFloat() < alternateChance;
                    var provider = useAlternate ? alternate : inner;
                    FeatureHelpers.SafeSetBlock(level, position, State(provider, level, random, position), replace);

                    if ((!requireAlternate || useAlternate) && random.NextFloat() < placementChance)
                    {
                        buds.Add(position);
                    }
                }
                else
                {
                    var provider = shell >= middleThreshold ? middle : outer;
                    FeatureHelpers.SafeSetBlock(level, position, State(provider, level, random, position), replace);
                }
            }

            foreach (var position in buds)
            {
                var bud = placements[random.NextInt(placements.Length)];

                foreach (var face in FeatureHelpers.Directions)
                {
                    if (bud.HasProperty("facing"))
                    {
                        bud = bud.WithProperty("facing", FeatureHelpers.FaceName(face));
                    }

                    var target = position.Offset(face);
                    var existing = level.GetBlock(target);

                    if (bud.HasProperty("waterlogged"))
                    {
                        bud = bud.WithProperty("waterlogged", existing.IsFluidSource());
                    }

                    if (existing.IsAir || existing.Is(BlockIds.Water) && existing.FluidAmount() == 8)
                    {
                        FeatureHelpers.SafeSetBlock(level, target, bud, replace);
                        break;
                    }
                }
            }

            return true;
        };
    }

    private static double DoubleOr(JsonElement json, string name, double fallback) =>
        json.TryGetProperty(name, out var value) ? value.GetDouble() : fallback;
}
