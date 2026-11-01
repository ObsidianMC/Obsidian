using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>Samples an int provider.</summary>
internal delegate int IntProvider(IRandomSource random);

/// <summary>Samples a float provider.</summary>
internal delegate float FloatProvider(IRandomSource random);

/// <summary>Samples a height provider for a generator's build range.</summary>
internal delegate int HeightProvider(IRandomSource random, int minY, int height);

// 26.3's int, float and height providers. Each sample draws exactly vanilla's randoms in vanilla's order, since
// features share one random. A type that isn't vanilla's is one a plugin registered (see FeatureRegistry.PluginValues).
internal sealed partial class FeatureRegistry
{
    /// <summary>An int provider: a number for a constant, one of 26.3's types or a type a plugin registered.</summary>
    public IntProvider Int(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
        {
            var value = json.GetInt32();
            return _ => value;
        }

        var type = json.GetProperty("type").GetString();

        switch (type)
        {
            case "minecraft:constant":
            {
                var value = json.GetProperty("value").GetInt32();
                return _ => value;
            }
            case "minecraft:uniform":
            {
                var min = json.GetProperty("min_inclusive").GetInt32();
                var max = json.GetProperty("max_inclusive").GetInt32();
                return random => random.NextIntBetweenInclusive(min, max);
            }
            case "minecraft:biased_to_bottom":
            {
                var min = json.GetProperty("min_inclusive").GetInt32();
                var max = json.GetProperty("max_inclusive").GetInt32();
                return random => min + random.NextInt(random.NextInt(max - min + 1) + 1);
            }
            case "minecraft:clamped":
            {
                var source = this.Int(json.GetProperty("source"));
                var min = json.GetProperty("min_inclusive").GetInt32();
                var max = json.GetProperty("max_inclusive").GetInt32();
                return random => Math.Clamp(source(random), min, max);
            }
            case "minecraft:clamped_normal":
            {
                var mean = json.GetProperty("mean").GetSingle();
                var deviation = json.GetProperty("deviation").GetSingle();
                var min = (float)json.GetProperty("min_inclusive").GetInt32();
                var max = (float)json.GetProperty("max_inclusive").GetInt32();
                return random => (int)ValueProviders.ClampedNormal(random, mean, deviation, min, max);
            }
            case "minecraft:trapezoid":
                return ValueProviders.Trapezoid(
                    json.GetProperty("min").GetInt32(),
                    json.GetProperty("max").GetInt32(),
                    json.GetProperty("plateau").GetInt32());
            case "minecraft:weighted_list":
            {
                var entries = ValueProviders.Weighted(json.GetProperty("distribution"), this.Int);
                return random => entries.Pick(random)!(random);
            }
            default:
                return this.PluginInt(json, type);
        }
    }

    /// <summary>A float provider: a number for a constant, one of 26.3's types or a type a plugin registered.</summary>
    public FloatProvider Float(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
        {
            var value = json.GetSingle();
            return _ => value;
        }

        var type = json.GetProperty("type").GetString();

        switch (type)
        {
            case "minecraft:constant":
            {
                var value = json.GetProperty("value").GetSingle();
                return _ => value;
            }
            case "minecraft:uniform":
            {
                var min = json.GetProperty("min_inclusive").GetSingle();
                var max = json.GetProperty("max_exclusive").GetSingle();

                // Mth.randomBetween.
                return random => random.NextFloat() * (max - min) + min;
            }
            case "minecraft:clamped_normal":
            {
                var mean = json.GetProperty("mean").GetSingle();
                var deviation = json.GetProperty("deviation").GetSingle();
                var min = json.GetProperty("min").GetSingle();
                var max = json.GetProperty("max").GetSingle();
                return random => ValueProviders.ClampedNormal(random, mean, deviation, min, max);
            }
            default:
                return this.PluginFloat(json, type);
        }
    }

    /// <summary>
    /// A height provider: a vertical anchor for a constant, one of 26.3's types or a type a plugin registered.
    /// </summary>
    public HeightProvider Height(JsonElement json)
    {
        if (!json.TryGetProperty("type", out var typeJson))
        {
            var anchor = ValueProviders.Anchor(json);
            return (_, minY, height) => anchor(minY, height);
        }

        var type = typeJson.GetString();

        switch (type)
        {
            case "minecraft:constant":
            {
                var anchor = ValueProviders.Anchor(json.GetProperty("value"));
                return (_, minY, height) => anchor(minY, height);
            }
            case "minecraft:uniform":
            {
                var low = ValueProviders.Anchor(json.GetProperty("min_inclusive"));
                var high = ValueProviders.Anchor(json.GetProperty("max_inclusive"));
                return (random, minY, height) =>
                {
                    var min = low(minY, height);
                    var max = high(minY, height);

                    // Vanilla returns the minimum of an empty range.
                    return min > max ? min : random.NextIntBetweenInclusive(min, max);
                };
            }
            case "minecraft:trapezoid":
            {
                var low = ValueProviders.Anchor(json.GetProperty("min_inclusive"));
                var high = ValueProviders.Anchor(json.GetProperty("max_inclusive"));
                var plateau = json.TryGetProperty("plateau", out var plateauJson) ? plateauJson.GetInt32() : 0;
                return (random, minY, height) =>
                {
                    var min = low(minY, height);
                    var max = high(minY, height);

                    return min > max ? min : ValueProviders.TrapezoidHeight(random, min, max, plateau);
                };
            }
            case "minecraft:biased_to_bottom":
            case "minecraft:very_biased_to_bottom":
                return ValueProviders.BiasedHeight(json, type == "minecraft:very_biased_to_bottom");
            default:
                return this.PluginHeight(json, type);
        }
    }
}

/// <summary>
/// The parts of 26.3's value providers that don't depend on a registry: vertical anchors, weighted lists and the
/// draws of the providers with more than one.
/// </summary>
internal static class ValueProviders
{
    /// <summary>A vertical anchor, resolved against a build range's minimum Y and height.</summary>
    public static Func<int, int, int> Anchor(JsonElement json)
    {
        if (json.TryGetProperty("absolute", out var absolute))
        {
            var y = absolute.GetInt32();
            return (_, _) => y;
        }

        if (json.TryGetProperty("above_bottom", out var aboveBottom))
        {
            var offset = aboveBottom.GetInt32();
            return (minY, _) => minY + offset;
        }

        var belowTop = json.GetProperty("below_top").GetInt32();
        return (minY, height) => minY + height - 1 - belowTop;
    }

    public static WeightedEntries<T> Weighted<T>(JsonElement json, Func<JsonElement, T> read)
    {
        var entries = new List<(T Value, int Weight)>();

        foreach (var entry in json.EnumerateArray())
        {
            entries.Add((read(entry.GetProperty("data")), entry.GetProperty("weight").GetInt32()));
        }

        return new WeightedEntries<T>(entries);
    }

    /// <summary>TrapezoidInt: a symmetric triangle draws twice from its positive half, others in two parts.</summary>
    public static IntProvider Trapezoid(int min, int max, int plateau)
    {
        if (plateau == 0 && min == -max)
        {
            return random => random.NextInt(max + 1) - random.NextInt(max + 1);
        }

        var range = max - min;

        if (plateau == range)
        {
            return random => random.NextIntBetweenInclusive(min, max);
        }

        var slope = (range - plateau) / 2;
        var rest = range - slope;
        return random => min + random.NextIntBetweenInclusive(0, rest) + random.NextIntBetweenInclusive(0, slope);
    }

    // TrapezoidHeight: like TrapezoidInt's general case, without the symmetric shortcut.
    public static int TrapezoidHeight(IRandomSource random, int min, int max, int plateau)
    {
        var range = max - min;

        if (plateau >= range)
        {
            return random.NextIntBetweenInclusive(min, max);
        }

        var slope = (range - plateau) / 2;
        var rest = range - slope;
        return min + random.NextIntBetweenInclusive(0, rest) + random.NextIntBetweenInclusive(0, slope);
    }

    public static HeightProvider BiasedHeight(JsonElement json, bool very)
    {
        var low = Anchor(json.GetProperty("min_inclusive"));
        var high = Anchor(json.GetProperty("max_inclusive"));
        var inner = json.TryGetProperty("inner", out var innerJson) ? innerJson.GetInt32() : 1;

        return (random, minY, height) =>
        {
            var min = low(minY, height);
            var max = high(minY, height);

            if (max - min - inner + 1 <= 0)
            {
                return min;
            }

            if (!very)
            {
                var spread = random.NextInt(max - min - inner + 1);
                return random.NextInt(spread + inner) + min;
            }

            var first = NextInt(random, min + inner, max);
            var second = NextInt(random, min, first - 1);
            return NextInt(random, min, second - 1 + inner);
        };
    }

    // Mth.nextInt, which returns the minimum of an empty range without drawing.
    private static int NextInt(IRandomSource random, int min, int max) =>
        min >= max ? min : random.NextInt(max - min + 1) + min;

    // Mth.normal then Mth.clamp, in float.
    public static float ClampedNormal(IRandomSource random, float mean, float deviation, float min, float max)
    {
        var value = mean + (float)random.NextGaussian() * deviation;

        return Math.Clamp(value, min, max);
    }
}

/// <summary>Vanilla's WeightedList: one draw below the total weight, then the entry whose range holds it.</summary>
internal sealed class WeightedEntries<T>(List<(T Value, int Weight)> entries)
{
    private readonly int total = entries.Sum(entry => entry.Weight);

    public IReadOnlyList<(T Value, int Weight)> Entries => entries;

    /// <summary>The picked value, or the default for an empty list, which draws nothing.</summary>
    public T? Pick(IRandomSource random)
    {
        if (this.total == 0)
        {
            return default;
        }

        var target = random.NextInt(this.total);

        foreach (var (value, weight) in entries)
        {
            target -= weight;

            if (target < 0)
            {
                return value;
            }
        }

        throw new InvalidOperationException("The weights changed.");
    }
}
