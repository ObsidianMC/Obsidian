using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>
/// A block-state provider's <c>getOptionalState</c>: the state for a position, or null where a rule-based provider
/// has no rule or fallback for it.
/// </summary>
internal delegate IBlock? StateProvider(IWorldGenLevel level, IRandomSource random, Vector position);

internal sealed partial class FeatureRegistry
{
    private readonly Dictionary<string, StateProvider> providers = [];

    /// <summary>
    /// A provider: a reference into the <c>block_state_provider</c> registry, a typed provider, or a block state, which
    /// is a simple provider.
    /// </summary>
    public StateProvider Provider(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            var id = json.GetString()!;

            if (!this.providers.TryGetValue(id, out var provider))
            {
                provider = this.Provider(this.Data.Read("block_state_provider", id));
                this.providers.Add(id, provider);
            }

            return provider;
        }

        if (!json.TryGetProperty("type", out var typeJson))
        {
            var block = ReadBlock(json);
            return (_, _, _) => block;
        }

        var type = typeJson.GetString();

        switch (type)
        {
            case "minecraft:simple_state_provider":
            {
                var block = ReadBlock(json.GetProperty("state"));
                return (_, _, _) => block;
            }
            case "minecraft:weighted":
            {
                var entries = ValueProviders.Weighted(json.GetProperty("entries"), ReadBlock);
                return (_, random, _) => entries.Pick(random);
            }
            case "minecraft:rotated":
            {
                var block = ReadBlock(json.GetProperty("state"));

                // Direction.Axis.getRandom picks from X, Y, Z.
                string[] axes = ["x", "y", "z"];
                return (_, random, _) => block.WithProperty("axis", axes[random.NextInt(axes.Length)]);
            }
            case "minecraft:randomized_int":
                return this.RandomizedInt(json);
            case "minecraft:rule_based":
                return this.RuleBased(json);
            case "minecraft:noise_threshold":
                return NoiseThreshold(json);
            default:
                throw new NotSupportedException($"The 26.3 block state provider {type} isn't implemented yet.");
        }
    }

    /// <summary>A provider's <c>getState</c>: rule-based providers without a state keep the existing block.</summary>
    public static IBlock State(StateProvider provider, IWorldGenLevel level, IRandomSource random, Vector position) =>
        provider(level, random, position) ?? level.GetBlock(position);

    private StateProvider RandomizedInt(JsonElement json)
    {
        var source = this.Provider(json.GetProperty("source"));
        var property = json.GetProperty("property").GetString()!;
        var values = ValueProviders.Int(json.GetProperty("values"));

        return (level, random, position) =>
        {
            var state = source(level, random, position);

            if (state is null)
            {
                return null;
            }

            var value = values(random);
            return state.WithProperty(property, value.ToString());
        };
    }

    private StateProvider RuleBased(JsonElement json)
    {
        var rules = new List<(BlockPredicate Test, StateProvider Then)>();

        foreach (var rule in json.GetProperty("rules").EnumerateArray())
        {
            rules.Add((this.Predicate(rule.GetProperty("if_true")), this.Provider(rule.GetProperty("then"))));
        }

        StateProvider? fallback = null;

        if (json.TryGetProperty("fallback", out var fallbackJson))
        {
            fallback = this.Provider(fallbackJson);
        }

        return (level, random, position) =>
        {
            foreach (var (test, then) in rules)
            {
                if (!test(level, position))
                {
                    continue;
                }

                var state = then(level, random, position);

                if (state is not null)
                {
                    return state;
                }
            }

            return fallback?.Invoke(level, random, position);
        };
    }

    // Low states below the threshold, otherwise a high state by chance, otherwise the default. The noise is the
    // provider's own, seeded from its seed with a legacy random like NoiseBasedStateProvider's.
    private static StateProvider NoiseThreshold(JsonElement json)
    {
        var noise = ProviderNoise(json);
        var scale = (double)json.GetProperty("scale").GetSingle();
        var threshold = (double)json.GetProperty("threshold").GetSingle();
        var highChance = json.GetProperty("high_chance").GetSingle();
        var defaultState = ReadBlock(json.GetProperty("default_state"));
        var lowStates = json.GetProperty("low_states").EnumerateArray().Select(ReadBlock).ToArray();
        var highStates = json.GetProperty("high_states").EnumerateArray().Select(ReadBlock).ToArray();

        return (_, random, position) =>
        {
            var value = (double)noise.Get(position.X * scale, position.Y * scale, position.Z * scale);

            if (value < threshold)
            {
                return lowStates[random.NextInt(lowStates.Length)];
            }

            if (random.NextFloat() < highChance)
            {
                return highStates[random.NextInt(highStates.Length)];
            }

            return defaultState;
        };
    }

    private static NoiseStack ProviderNoise(JsonElement json)
    {
        var seed = json.GetProperty("seed").GetInt64();
        var parameters = DensityCompiler.ReadNoise(json.GetProperty("noise"), "block_state_provider");
        var random = new WorldgenRandom(new LegacyRandomSource(seed));

        return parameters.Create(random);
    }
}
