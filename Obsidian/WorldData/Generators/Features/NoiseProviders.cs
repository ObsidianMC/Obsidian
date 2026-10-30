using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static StateProvider NoiseProvider(JsonElement json)
    {
        var noise = ProviderNoise(json);
        var scale = (double)json.GetProperty("scale").GetSingle();
        var states = json.GetProperty("states").EnumerateArray().Select(ReadBlock).ToArray();

        return (_, _, position) =>
        {
            var value = noise.Get(position.X * scale, position.Y * scale, position.Z * scale);
            return NoiseState(states, value);
        };
    }

    private static StateProvider DualNoiseProvider(JsonElement json)
    {
        var fastNoise = ProviderNoise(json);
        var scale = (double)json.GetProperty("scale").GetSingle();
        var slowScale = json.GetProperty("slow_scale").GetSingle();
        var seed = json.GetProperty("seed").GetInt64();
        var parameters = DensityCompiler.ReadNoise(json.GetProperty("slow_noise"), "block_state_provider");
        var slowNoise = parameters.Create(new WorldgenRandom(new LegacyRandomSource(seed)));
        var states = json.GetProperty("states").EnumerateArray().Select(ReadBlock).ToArray();
        var variety = json.GetProperty("variety");
        var minimum = variety.GetProperty("min_inclusive").GetInt32();
        var maximum = variety.GetProperty("max_inclusive").GetInt32();

        // Unlike the fast noise's double multiplication, the slow coordinates multiply in float in 26.3.
        float SlowValue(Vector position) => slowNoise.Get(
            position.X * slowScale, position.Y * slowScale, position.Z * slowScale);

        return (_, _, position) =>
        {
            var slowValue = (double)SlowValue(position);
            var fraction = Math.Clamp((slowValue + 1.0) / 2.0, 0.0, 1.0);
            var count = (int)(minimum + fraction * (maximum + 1 - minimum));
            var palette = new IBlock[count];

            for (var i = 0; i < count; i++)
            {
                // These offsets match DualNoiseProvider's spatial palette decorrelation constants.
                var offset = position + new Vector(unchecked(i * 54545), 0, unchecked(i * 34234));
                palette[i] = NoiseState(states, SlowValue(offset));
            }

            var value = fastNoise.Get(position.X * scale, position.Y * scale, position.Z * scale);
            return NoiseState(palette, value);
        };
    }

    private static IBlock NoiseState(IBlock[] states, float noise)
    {
        var fraction = Math.Clamp((1.0f + noise) / 2.0f, 0.0f, 0.9999f);
        return states[(int)(fraction * states.Length)];
    }
}
