using Obsidian.API.World.Generator.DensityFunctions;
using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.SurfaceConditions;
using System.Globalization;
using System.IO;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;
using SpawnTarget = Obsidian.API.World.Generator.Noise.ClimateParameter;
using SurfaceAnchor = Obsidian.API.World.Generator.SurfaceConditions.VerticalAnchor;

namespace Obsidian.WorldData.Features;

// The values of noise settings and density functions that aren't read as plain objects, with the checks vanilla's
// codecs make where they're cheap.
internal sealed partial class WorldgenReader
{
    // Vanilla's bounds for density function values, and for the heights density functions and anchors name: twice
    // and once the range dimension types can have.
    private const double MaxNoiseValue = 1_000_000.0;
    private const int MinFunctionY = -4064;
    private const int MaxFunctionY = 4062;
    private const int MinAnchorY = -2032;
    private const int MaxAnchorY = 2031;

    // Vanilla's NoiseGeneratorSettings codec requires every field.
    private static readonly string[] noiseSettingsFields =
    [
        "sea_level",
        "disable_mob_generation",
        "aquifers_enabled",
        "ore_veins_enabled",
        "legacy_random_source",
        "default_block",
        "default_fluid",
        "noise",
        "noise_router",
        "surface_rule",
        "spawn_target"
    ];

    private static readonly string[] climateFields =
    [
        "temperature",
        "humidity",
        "continentalness",
        "erosion",
        "depth",
        "weirdness",
        "offset"
    ];

    /// <summary>
    /// Reads a noise, <c>{"firstOctave": ..., "amplitudes": [...]}</c>.
    /// </summary>
    /// <param name="key">The noise's identifier, which seeds it.</param>
    /// <param name="path">Where the value is in its file, for messages.</param>
    public static BaseNoise ReadNoise(JsonElement json, string key, string path = "") =>
        Guard(path, () => ReadNoiseCore(json, key, path));

    // The value of a noise settings or density function type, or null for other types.
    private object? ReadNoiseValue(Type target, JsonElement json, string path)
    {
        if (target == typeof(IDensityFunction))
            return this.ReadDensityFunction(json, path);

        if (target == typeof(ISurfaceRule) && this.SurfaceRules is Func<JsonElement, string, ISurfaceRule> rule)
            return rule(json, path);

        if (target == typeof(ISurfaceCondition)
            && this.SurfaceConditions is Func<JsonElement, string, ISurfaceCondition> condition)
        {
            return condition(json, path);
        }

        // The hook sees every noise-typed property, including a plugin's own BaseNoise subclass, which would otherwise
        // fall through to plain deserialization and build a noise no generator seeds.
        if (this.Noises is Func<JsonElement, Type, string, BaseNoise> readNoise
            && typeof(INoise).IsAssignableFrom(target))
        {
            return readNoise(json, target, path);
        }

        if (target == typeof(INoise) || target == typeof(BaseNoise))
        {
            return json.ValueKind == JsonValueKind.Object
                ? ReadInlineNoise(json, path)
                : Lookup(this.noise, Reference(json, path), path);
        }

        if (target == typeof(ISpline))
        {
            return json.ValueKind == JsonValueKind.Number
                ? new SplineConstant { Value = Double(json, path) }
                : this.ReadSpline(json, path);
        }

        if (target == typeof(Spline))
            return this.ReadSpline(json, path);

        if (target == typeof(SurfaceAnchor))
            return ReadSurfaceAnchor(json, path);

        if (target == typeof(SpawnTarget))
            return ReadSpawnTarget(json, path);

        if (target == typeof(NoiseSetting))
            return this.ReadNoiseSettings(json, path);

        if (target == typeof(NoiseConfiguration))
            return CheckNoise((NoiseConfiguration)this.ReadObject(target, json, null, null, path), path);

        return null;
    }

    // Like vanilla's density function codec: a number is a constant, a string a registered function.
    private IDensityFunction ReadDensityFunction(JsonElement json, string path)
    {
        if (this.DensityFunctions is Func<JsonElement, string, IDensityFunction> read)
            return read(json, path);

        if (json.ValueKind == JsonValueKind.Number)
        {
            return new ConstantDensityFunction
            {
                Argument = Double(json, path, -MaxNoiseValue, MaxNoiseValue)
            };
        }

        if (json.ValueKind == JsonValueKind.String)
            return Lookup(this.densityFunction, Reference(json, path), path);

        return (IDensityFunction)this.ReadPolymorphic(typeof(IDensityFunction), json, path);
    }

    // A multipoint spline. Vanilla also takes a number for a constant one, which an ISpline can be (a spline
    // function's spline, or a point's value), but not a property typed Spline, which plugins' types may have.
    private Spline ReadSpline(JsonElement json, string path)
    {
        if (json.ValueKind == JsonValueKind.Number)
            throw Error(path, "expected a multipoint spline, not a constant");

        var spline = (Spline)this.ReadObject(typeof(Spline), json, null, null, path);

        return spline.Points.IsEmpty ? throw Error(Field(path, "points"), "can't be empty") : spline;
    }

    private NoiseSetting ReadNoiseSettings(JsonElement json, string path)
    {
        Object(json, path);

        foreach (var field in noiseSettingsFields)
            Required(json, path, field);

        return (NoiseSetting)this.ReadObject(typeof(NoiseSetting), json, null, null, path);
    }

    // The ranges vanilla's codecs give the values of density functions and surface conditions. Generation relies on
    // them: a find_top_surface with no cell height would never end, for one.
    private static void CheckParameters(object value, string path)
    {
        switch (value)
        {
            case ConstantDensityFunction constant:
                CheckNoiseValue(constant.Argument, path, "argument");
                break;
            case ClampDensityFunction clamp:
                CheckNoiseValue(clamp.Min, path, "min");
                CheckNoiseValue(clamp.Max, path, "max");
                break;
            case RangeChoiceDensityFunction choice:
                CheckNoiseValue(choice.MinInclusive, path, "min_inclusive");
                CheckNoiseValue(choice.MaxExclusive, path, "max_exclusive");
                break;
            case YClampedGradientDensityFunction gradient:
                CheckRange(gradient.FromY, MinFunctionY, MaxFunctionY, path, "from_y");
                CheckRange(gradient.ToY, MinFunctionY, MaxFunctionY, path, "to_y");
                CheckNoiseValue(gradient.FromValue, path, "from_value");
                CheckNoiseValue(gradient.ToValue, path, "to_value");
                break;
            case FindTopSurface surface:
                CheckRange(surface.LowerBound, MinFunctionY, MaxFunctionY, path, "lower_bound");

                if (surface.CellHeight < 1)
                    throw Error(Field(path, "cell_height"), $"{surface.CellHeight} isn't positive");

                break;
            case OldBlendedNoiseDensityFunction blended:
                CheckRange(blended.XzScale, 0.001, 1000.0, path, "xz_scale");
                CheckRange(blended.YScale, 0.001, 1000.0, path, "y_scale");
                CheckRange(blended.XzFactor, 0.001, 1000.0, path, "xz_factor");
                CheckRange(blended.YFactor, 0.001, 1000.0, path, "y_factor");
                CheckRange(blended.SmearScaleMultiplier, 1.0, 8.0, path, "smear_scale_multiplier");
                break;
            case WaterSurfaceCondition water:
                CheckRange(water.SurfaceDepthMultiplier, -20, 20, path, "surface_depth_multiplier");
                break;
            case YAboveSurfaceCondition above:
                CheckRange(above.SurfaceDepthMultiplier, -20, 20, path, "surface_depth_multiplier");
                break;
            default:
                CheckStructureParameters(value, path);
                break;
        }
    }

    private static void CheckNoiseValue(double value, string path, string field) =>
        CheckRange(value, -MaxNoiseValue, MaxNoiseValue, path, field);

    private static void CheckRange(double value, double min, double max, string path, string field)
    {
        if (value < min || value > max)
            throw Error(Field(path, field), FormattableString.Invariant($"{value} isn't from {min} to {max}"));
    }

    // Vanilla's NoiseSettings codec: the noise's cells must lie in the range dimension types can have.
    private static NoiseConfiguration CheckNoise(NoiseConfiguration noise, string path)
    {
        if (noise.MinY is < -2032 or > 2031)
            throw Error(Field(path, "min_y"), $"{noise.MinY} isn't from -2032 to 2031");

        if (noise.Height is < 0 or > 4064)
            throw Error(Field(path, "height"), $"{noise.Height} isn't from 0 to 4064");

        if (noise.SizeHorizontal is < 1 or > 4)
            throw Error(Field(path, "size_horizontal"), $"{noise.SizeHorizontal} isn't from 1 to 4");

        if (noise.SizeVertical is < 1 or > 4)
            throw Error(Field(path, "size_vertical"), $"{noise.SizeVertical} isn't from 1 to 4");

        if (noise.MinY + noise.Height > 2032)
            throw Error(path, "min_y + height cannot be higher than 2032");

        if (noise.Height % 16 != 0)
            throw Error(Field(path, "height"), "has to be a multiple of 16");

        if (noise.MinY % 16 != 0)
            throw Error(Field(path, "min_y"), "has to be a multiple of 16");

        return noise;
    }

    private static BaseNoise ReadNoiseCore(JsonElement json, string key, string path)
    {
        Object(json, path);

        foreach (var property in json.EnumerateObject())
        {
            if (property.Name is not ("firstOctave" or "amplitudes"))
                throw Error(Field(path, property.Name), "a noise has no such field");
        }

        var amplitudesPath = Field(path, "amplitudes");
        var amplitudes = Array(Required(json, path, "amplitudes"), amplitudesPath)
            .EnumerateArray()
            .Select((amplitude, i) => Double(amplitude, Index(amplitudesPath, i)))
            .ToImmutableArray();

        if (amplitudes.IsEmpty)
            throw Error(amplitudesPath, "can't be empty");

        return new BaseNoise
        {
            Key = key,
            FirstOctave = Int(Required(json, path, "firstOctave"), Field(path, "firstOctave")),
            Amplitudes = amplitudes
        };
    }

    // A noise defined inline, which vanilla's codec reads but its generator can't seed, as it seeds noises from their
    // identifiers. Obsidian seeds it from its parameters instead, so inline noises with the same parameters are one
    // noise, like noises of one identifier. The key isn't an identifier, so it never seeds like a registered noise.
    // It's formatted invariantly, as the seeds must be the same whatever the server's culture.
    private static BaseNoise ReadInlineNoise(JsonElement json, string path)
    {
        var noise = ReadNoiseCore(json, "", path);
        var amplitudes = noise.Amplitudes.Select(amplitude => amplitude.ToString("R", CultureInfo.InvariantCulture));
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"obsidian:inline noise {noise.FirstOctave} [{string.Join(',', amplitudes)}]");

        return new BaseNoise
        {
            Key = key,
            FirstOctave = noise.FirstOctave,
            Amplitudes = noise.Amplitudes
        };
    }

    // Vanilla's VerticalAnchor codec: exactly one of its three forms, in the range dimension types can have.
    private static SurfaceAnchor ReadSurfaceAnchor(JsonElement json, string path)
    {
        const string expected = "expected one of absolute, above_bottom or below_top";

        var properties = Object(json, path).EnumerateObject().ToList();
        if (properties.Count != 1)
            throw Error(path, expected);

        var property = properties[0];
        var value = Int(property.Value, Field(path, property.Name), MinAnchorY, MaxAnchorY);

        return property.Name switch
        {
            "absolute" => new SurfaceAnchor { Absolute = value },
            "above_bottom" => new SurfaceAnchor { AboveBottom = value },
            "below_top" => new SurfaceAnchor { BelowTop = value },
            _ => throw Error(Field(path, property.Name), expected)
        };
    }

    // A spawn target, vanilla's Climate.ParameterPoint: each parameter a number from -2 to 2 or a range, [min, max]
    // or {"min": ..., "max": ...}, and an offset from 0 to 1.
    private static SpawnTarget ReadSpawnTarget(JsonElement json, string path)
    {
        Object(json, path);

        foreach (var property in json.EnumerateObject())
        {
            if (!climateFields.Contains(property.Name))
                throw Error(Field(path, property.Name), "a climate point has no such field");
        }

        ImmutableArray<double> Range(string name) =>
            ReadClimateRange(Required(json, path, name), Field(path, name));

        return new SpawnTarget
        {
            Temperature = Range("temperature"),
            Humidity = Range("humidity"),
            Continentalness = Range("continentalness"),
            Erosion = Range("erosion"),
            Depth = Range("depth"),
            Weirdness = Range("weirdness"),
            Offset = Double(Required(json, path, "offset"), Field(path, "offset"), 0.0, 1.0)
        };
    }

    private static ImmutableArray<double> ReadClimateRange(JsonElement json, string path)
    {
        double min;
        double max;

        switch (json.ValueKind)
        {
            case JsonValueKind.Number:
                min = Double(json, path, -2.0, 2.0);
                max = min;
                break;
            case JsonValueKind.Array when json.GetArrayLength() == 2:
                min = Double(json[0], Index(path, 0), -2.0, 2.0);
                max = Double(json[1], Index(path, 1), -2.0, 2.0);
                break;
            case JsonValueKind.Object:
                min = Double(Required(json, path, "min"), Field(path, "min"), -2.0, 2.0);
                max = Double(Required(json, path, "max"), Field(path, "max"), -2.0, 2.0);
                break;
            default:
                throw Error(path, "expected a number from -2 to 2, [min, max] or {\"min\": ..., \"max\": ...}");
        }

        return min <= max ? [min, max] : throw Error(path, $"min {min} is above max {max}");
    }

    private static IDensityFunction VanillaDensityFunction(string identifier) =>
        NoiseRegistry.DensityFunctions.All.TryGetValue(identifier, out var function)
            ? function
            : throw new InvalidDataException($"{identifier} isn't a density function");

    private static BaseNoise VanillaNoise(string identifier) =>
        NoiseRegistry.Noises.All.TryGetValue(identifier, out var noise)
            ? noise
            : throw new InvalidDataException($"{identifier} isn't a noise");
}
