using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;
using static Obsidian.WorldData.Generators.Density.DensityJson;

namespace Obsidian.WorldData.Generators.Density;

// Noises: the noise JSON and old_blended_noise's octaves.
internal sealed partial class DensityCompiler
{
    private static readonly string[] noiseFields =
    [
        "base_octave",
        "base_amplitude",
        "octave_count",
        "normalize",
        "amplitude_modifiers"
    ];

    /// <summary>
    /// Reads a 26.3 noise with vanilla's defaults (amplitude 1, one octave, normalized, no modifiers) and checks.
    /// </summary>
    /// <param name="path">Where the value is, for messages.</param>
    public static NoiseParameters ReadNoise(JsonElement json, string path)
    {
        Object(json, path);

        foreach (var property in json.EnumerateObject())
        {
            if (!noiseFields.Contains(property.Name))
                throw Error(Field(path, property.Name), "a noise has no such field");
        }

        var baseOctave = Int(Required(json, path, "base_octave"), Field(path, "base_octave"), -32, 32);
        var baseAmplitude = OptionalDouble(json, path, "base_amplitude", 1.0, 1.0E-5f, 1_000_000.0);
        var octaveCount = json.TryGetProperty("octave_count", out var count)
            ? Int(count, Field(path, "octave_count"), 1, 32)
            : 1;

        var normalize = json.TryGetProperty("normalize", out var normalizeJson)
            ? ReadNormalization(normalizeJson, Field(path, "normalize"))
            : NoiseNormalization.Enabled;

        var modifiersPath = Field(path, "amplitude_modifiers");
        var modifiers = json.TryGetProperty("amplitude_modifiers", out var modifiersJson)
            ? Array(modifiersJson, modifiersPath)
                .Select((modifier, i) => Double(modifier, Index(modifiersPath, i), 0.0, 1_000_000.0))
                .ToImmutableArray()
            : [];

        if (!modifiers.IsEmpty && modifiers.Length != octaveCount)
            throw Error(modifiersPath, $"had size {modifiers.Length}, but octave_count was {octaveCount}");

        return new NoiseParameters(baseOctave, baseAmplitude, octaveCount, normalize, modifiers);
    }

    // A boolean, or "legacy".
    private static NoiseNormalization ReadNormalization(JsonElement json, string path)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.True:
                return NoiseNormalization.Enabled;
            case JsonValueKind.False:
                return NoiseNormalization.Disabled;
            case JsonValueKind.String when json.GetString() == "legacy":
                return NoiseNormalization.Legacy;
            default:
                throw Error(path, "expected true, false or \"legacy\"");
        }
    }

    private static double OptionalDouble(JsonElement json, string path, string field, double fallback, double min,
        double max)
    {
        if (!json.TryGetProperty(field, out var value))
            return fallback;

        return Double(value, Field(path, field), min, max);
    }

    /// <summary>
    /// <c>old_blended_noise</c>: two smeared limit noises blended by a main noise, built from vanilla's composition of
    /// noise, add, clamp and lerp so that both sampling paths match.
    /// </summary>
    private LerpFunction CompileBlendedNoise(JsonElement json, string path)
    {
        var xzScale = Double(Required(json, path, "xz_scale"), Field(path, "xz_scale"), 0.001, 1000.0);
        var yScale = Double(Required(json, path, "y_scale"), Field(path, "y_scale"), 0.001, 1000.0);
        var xzFactor = Double(Required(json, path, "xz_factor"), Field(path, "xz_factor"), 0.001, 1000.0);
        var yFactor = Double(Required(json, path, "y_factor"), Field(path, "y_factor"), 0.001, 1000.0);
        var smearPath = Field(path, "smear_scale_multiplier");
        var smear = Double(Required(json, path, "smear_scale_multiplier"), smearPath, 1.0, 8.0);

        // Vanilla's base scale of 684.412 and its octave sets; 0.99998474F is a float literal widened to double.
        var xzMultiplier = 684.412 * xzScale;
        var yMultiplier = 684.412 * yScale;
        var limitSmearScaleY = yMultiplier * smear;
        var mainSmearScaleY = limitSmearScaleY / yFactor;

        var random = this.TerrainRandom();
        var minLimit = CreateSmearedFbm(random, -15, limitSmearScaleY, (double)0.99998474f);
        var maxLimit = CreateSmearedFbm(random, -15, limitSmearScaleY, (double)0.99998474f);
        var main = CreateSmearedFbm(random, -7, mainSmearScaleY, 12.75);

        var minLimitNoise = new NoiseFunction(minLimit, xzMultiplier, yMultiplier);
        var maxLimitNoise = new NoiseFunction(maxLimit, xzMultiplier, yMultiplier);
        var mainNoise = new NoiseFunction(main, xzMultiplier / xzFactor, yMultiplier / yFactor);

        var shifted = BinaryFunction.Create(BinaryOperation.Add, mainNoise, new ConstantFunction(0.5f));
        var choice = new ClampFunction(shifted, 0.0f, 1.0f);

        return new LerpFunction(choice, minLimitNoise, maxLimitNoise);
    }

    // Vanilla seeds the blended noise from minecraft:terrain, or from the world seed itself with the legacy source.
    private IRandomSource TerrainRandom()
    {
        if (this.legacyRandomSource)
            return new LegacyRandomSource(this.seed);

        return this.random.FromHashOf("minecraft:terrain");
    }

    // Octaves from the lowest frequency's amplitude down, drawn one after another from the same random.
    private static NoiseStack CreateSmearedFbm(IRandomSource random, int firstOctave, double smearScaleY,
        double valueFactor)
    {
        var octaves = -firstOctave + 1;
        var frequency = 1.0;
        var amplitude = valueFactor / (Math.Pow(2.0, octaves) - 1.0);
        var layers = new List<NoiseStack.Layer>(octaves);

        for (var i = octaves - 1; i >= 0; i--)
        {
            var noise = new SmearedPerlinNoise(random, smearScaleY * frequency);
            layers.Add(new NoiseStack.Layer(noise, frequency, (float)amplitude));

            frequency /= 2.0;
            amplitude *= 2.0;
        }

        return new NoiseStack(layers);
    }
}
