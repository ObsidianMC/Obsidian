using Obsidian.API.World.Generator.RandomSources;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using static Obsidian.WorldData.Generators.Density.DensityJson;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// Builds 26.3 density functions from JSON for one world, like vanilla's RandomState and DensityFunctionCompiler:
/// noises are seeded from their identifiers, references are inlined and caches only remember values.
/// </summary>
/// <remarks>
/// It checks what vanilla's codecs check where it's cheap (required fields, ranges, ordered thresholds). It doesn't
/// fold min and max by their inputs' ranges as vanilla does, which changes no value but NaN.
/// </remarks>
internal sealed partial class DensityCompiler
{
    // Vanilla's bounds for constants and values (NOISE_VALUE_CODEC) and for the heights functions name.
    private const float MaxNoiseValue = 1_000_000.0f;
    private const int MinFunctionY = -4064;
    private const int MaxFunctionY = 4062;

    private readonly WorldgenData data;
    private readonly long seed;
    private readonly bool legacyRandomSource;
    private readonly IPositionalRandomFactory random;
    private readonly DensityFunction? beardifier;
    private readonly IDensityTypes? types;
    private readonly ConcurrentDictionary<string, NoiseStack> noises = new();
    private readonly ConcurrentDictionary<string, DensityFunction> functions = new();

    // The registered functions this thread is compiling, outermost first (see EnterFunction). Entries leave as their
    // compile ends, so it holds no compiler once a compile is done.
    [ThreadStatic]
    private static List<(DensityCompiler Compiler, string Id)>? compilingFunctions;

    /// <param name="legacyRandomSource">The noise settings' <c>legacy_random_source</c>.</param>
    /// <param name="types">The density function types beyond vanilla's that the JSON may name, such as plugins'
    /// (see <see cref="PluginDensityTypes"/>); none by default.</param>
    public DensityCompiler(
        WorldgenData data,
        long seed,
        bool legacyRandomSource,
        DensityFunction? beardifier = null,
        IDensityTypes? types = null)
    {
        IRandomSource worldRandom = legacyRandomSource ? new LegacyRandomSource(seed) : new XoroshiroRandomSource(seed);

        this.data = data;
        this.beardifier = beardifier;
        this.types = types;
        this.seed = seed;
        this.legacyRandomSource = legacyRandomSource;
        this.random = worldRandom.ForkPositional();
    }

    public IPositionalRandomFactory Random => this.random;

    /// <summary>The named random sequence used by terrain's aquifers and materials.</summary>
    public IPositionalRandomFactory RandomFactory(string id) => this.random.FromHashOf(id).ForkPositional();

    /// <summary>A registered density function, such as <c>minecraft:overworld/continents</c>.</summary>
    /// <exception cref="InvalidDataException">It isn't valid, or refers back to itself.</exception>
    public DensityFunction Function(string id)
    {
        if (this.functions.TryGetValue(id, out var function))
            return function;

        var json = this.data.Read("density_function", id);
        var qualified = WorldgenData.Qualify(id);
        var compiling = this.EnterFunction(qualified);

        try
        {
            function = this.Compile(json, id);
        }
        finally
        {
            compiling.RemoveAt(compiling.Count - 1);
        }

        return this.functions.GetOrAdd(id, function);
    }

    // Notes that this thread compiles a registered function, inside those it's compiling already. Functions are only
    // cached once compiled, so one that refers back to itself, which data packs can write, would recurse until the
    // stack overflows: it's refused instead, naming the chain.
    private List<(DensityCompiler Compiler, string Id)> EnterFunction(string id)
    {
        var compiling = compilingFunctions ??= [];
        var start = compiling.FindIndex(entry => entry.Compiler == this && entry.Id == id);

        if (start >= 0)
        {
            var chain = compiling.Skip(start).Where(entry => entry.Compiler == this).Select(entry => entry.Id);
            throw new InvalidDataException($"{id} refers back to itself: {string.Join(" -> ", chain)} -> {id}");
        }

        compiling.Add((this, id));
        return compiling;
    }

    /// <summary>A registered noise, seeded for this world.</summary>
    public NoiseStack Noise(string id)
    {
        if (this.noises.TryGetValue(id, out var noise))
            return noise;

        var parameters = ReadNoise(this.data.Read("noise", id), id);

        // Vanilla gives the Nether's biome noises 1.16's octaves, seeded from the world seed (plus 1 for vegetation).
        noise = id switch
        {
            "minecraft:nether/temperature" => parameters.CreateLegacyNetherBiome(new LegacyRandomSource(this.seed)),
            "minecraft:nether/vegetation" => parameters.CreateLegacyNetherBiome(new LegacyRandomSource(this.seed + 1)),
            _ => parameters.Create(this.random.FromHashOf(id))
        };

        return this.noises.GetOrAdd(id, noise);
    }

    /// <summary>
    /// A density function: a number for a constant, a string for a registered function, or an object with a
    /// <c>type</c>.
    /// </summary>
    /// <param name="path">Where the value is, for messages.</param>
    public DensityFunction Compile(JsonElement json, string path)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Number:
                return new ConstantFunction(Float(json, path, -MaxNoiseValue, MaxNoiseValue));
            case JsonValueKind.String:
                return this.Function(json.GetString()!);
            case JsonValueKind.Object:
                return this.CompileObject(json, path);
            default:
                throw Error(path, "expected a number, a density function identifier or an object");
        }
    }

    private DensityFunction CompileObject(JsonElement json, string path)
    {
        var type = String(Required(json, path, "type"), Field(path, "type"));
        var name = type.StartsWith("minecraft:", StringComparison.Ordinal) ? type["minecraft:".Length..] : type;

        return name switch
        {
            "constant" => new ConstantFunction(this.NoiseValue(json, path, "value")),
            "blend_alpha" => new ContextFunction(name, 1.0f),
            "blend_offset" => new ContextFunction(name, 0.0f),
            "beardifier" => this.beardifier ?? new ContextFunction(name, 0.0f),
            "noise" => this.CompileNoise(json, path),
            "end_outer_islands" => new EndOuterIslandsFunction(this.seed),
            "distance_to_point" => CompileDistance(json, path),
            "gradient" => this.CompileGradient(json, path),
            "shift" => new ShiftFunction(this.NoiseField(json, path), ShiftKind.All),
            "shift_a" => new ShiftFunction(this.NoiseField(json, path), ShiftKind.A),
            "shift_b" => new ShiftFunction(this.NoiseField(json, path), ShiftKind.B),
            "abs" => this.Unary(UnaryOperation.Abs, json, path),
            "square" => this.Unary(UnaryOperation.Square, json, path),
            "cube" => this.Unary(UnaryOperation.Cube, json, path),
            "sqrt" => this.Unary(UnaryOperation.Sqrt, json, path),
            "half_negative" => this.Unary(UnaryOperation.HalfNegative, json, path),
            "quarter_negative" => this.Unary(UnaryOperation.QuarterNegative, json, path),
            "reciprocal" => this.Unary(UnaryOperation.Reciprocal, json, path),
            "negate" => this.Unary(UnaryOperation.Negate, json, path),
            "squeeze" => this.Unary(UnaryOperation.Squeeze, json, path),
            "log" => this.Unary(UnaryOperation.Log, json, path),
            "sign" => this.Unary(UnaryOperation.Sign, json, path),
            "floor" => this.Round(RoundingMode.Floor, json, path),
            "round" => this.Round(RoundingMode.Round, json, path),
            "ceil" => this.Round(RoundingMode.Ceil, json, path),
            "truncate" => this.Round(RoundingMode.Truncate, json, path),
            "add" => this.Binary(BinaryOperation.Add, json, path),
            "sub" => this.Binary(BinaryOperation.Sub, json, path),
            "mul" => this.Binary(BinaryOperation.Mul, json, path),
            "div" => this.Binary(BinaryOperation.Div, json, path),
            "min" => this.Binary(BinaryOperation.Min, json, path),
            "max" => this.Binary(BinaryOperation.Max, json, path),
            "pow" => PowFunction.Create(this.Input(json, path, "base"), this.Input(json, path, "exponent")),
            "spline" => new SplineFunction(this.CompileSpline(Required(json, path, "spline"), Field(path, "spline"))),
            "lerp" => this.CompileLerp(json, path),
            "clamp" => this.CompileClamp(json, path),
            "range_choice" => this.CompileRangeChoice(json, path),
            "interval_select" => this.CompileIntervalSelect(json, path),

            "cache" => Cache(this.Input(json, path, "input")),
            // Without a blender (bound to chunks later in Phase 5) blending doesn't change values.
            "blend_density" => this.Input(json, path, "input"),
            "interpolated" => this.CompileInterpolated(json, path),
            "slice" => this.CompileSlice(json, path),
            "find_top_surface" => this.CompileFindTopSurface(json, path),
            "old_blended_noise" => this.CompileBlendedNoise(json, path),
            _ => this.types?.Compile(type, json, path, this)
                ?? throw Error(Field(path, "type"), $"{type} isn't a density function type")
        };
    }

    // Caches don't change values; a constant stays one, so that operators still specialise on it.
    private static DensityFunction Cache(DensityFunction input) =>
        input is ConstantFunction ? input : new CacheFunction(input);

    private DensityFunction Input(JsonElement json, string path, string field) =>
        this.Compile(Required(json, path, field), Field(path, field));

    private float NoiseValue(JsonElement json, string path, string field) =>
        Float(Required(json, path, field), Field(path, field), -MaxNoiseValue, MaxNoiseValue);

    private NoiseStack NoiseField(JsonElement json, string path)
    {
        var noise = Required(json, path, "noise");
        if (noise.ValueKind != JsonValueKind.String)
            throw Error(Field(path, "noise"), "expected a noise identifier; inline noises aren't seeded by vanilla");

        return this.Noise(noise.GetString()!);
    }

    private NoiseFunction CompileNoise(JsonElement json, string path)
    {
        var noise = this.NoiseField(json, path);
        var xzScale = Double(Required(json, path, "xz_scale"), Field(path, "xz_scale"));
        var yScale = Double(Required(json, path, "y_scale"), Field(path, "y_scale"));
        var shiftX = this.OptionalShift(json, path, "shift_x");
        var shiftY = this.OptionalShift(json, path, "shift_y");
        var shiftZ = this.OptionalShift(json, path, "shift_z");

        return new NoiseFunction(noise, xzScale, yScale, shiftX, shiftY, shiftZ);
    }

    // An absent shift, or vanilla's zero constant, leaves the coordinate unshifted.
    private DensityFunction? OptionalShift(JsonElement json, string path, string field)
    {
        if (!json.TryGetProperty(field, out var value))
            return null;

        var shift = this.Compile(value, Field(path, field));
        var isZero = shift is ConstantFunction constant && constant.Value == 0.0f && !float.IsNegative(constant.Value);

        return isZero ? null : shift;
    }

    private static DistanceToPointFunction CompileDistance(JsonElement json, string path)
    {
        var pointPath = Field(path, "point");
        var point = Required(json, path, "point");
        if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 3)
            throw Error(pointPath, "expected [x, y, z]");

        var x = Int(point[0], Index(pointPath, 0));
        var y = Int(point[1], Index(pointPath, 1));
        var z = Int(point[2], Index(pointPath, 2));
        var metric = Enum(json, path, "metric", null, DistanceMetrics);

        return new DistanceToPointFunction(x, y, z, metric);
    }

    private GradientFunction CompileGradient(JsonElement json, string path)
    {
        var axis = Enum(json, path, "axis", null, Axes);
        var tiling = Enum(json, path, "tiling", GradientTiling.ClampToEdge, Tilings);
        var from = Int(Required(json, path, "from_coordinate"), Field(path, "from_coordinate"));
        var to = Int(Required(json, path, "to_coordinate"), Field(path, "to_coordinate"));

        if (from == to)
            throw Error(path, "from_coordinate cannot be equal to to_coordinate");

        var fromValue = this.NoiseValue(json, path, "from_value");
        var toValue = this.NoiseValue(json, path, "to_value");

        return new GradientFunction(axis, tiling, from, to, fromValue, toValue);
    }

    private UnaryFunction Unary(UnaryOperation operation, JsonElement json, string path) =>
        new(operation, this.Input(json, path, "input"));

    private RoundFunction Round(RoundingMode mode, JsonElement json, string path)
    {
        var input = this.Input(json, path, "input");
        var multiple = json.TryGetProperty("multiple", out var value)
            ? this.Compile(value, Field(path, "multiple"))
            : new ConstantFunction(1.0f);

        return new RoundFunction(mode, input, multiple);
    }

    private DensityFunction Binary(BinaryOperation operation, JsonElement json, string path)
    {
        var left = this.Input(json, path, "left");
        var right = this.Input(json, path, "right");

        return BinaryFunction.Create(operation, left, right);
    }

    private LerpFunction CompileLerp(JsonElement json, string path)
    {
        var alpha = this.Input(json, path, "alpha");
        var first = this.Input(json, path, "first");
        var second = this.Input(json, path, "second");

        return new LerpFunction(alpha, first, second);
    }

    private ClampFunction CompileClamp(JsonElement json, string path)
    {
        var input = this.Input(json, path, "input");
        var min = this.NoiseValue(json, path, "min");
        var max = this.NoiseValue(json, path, "max");

        if (min > max)
            throw Error(path, $"min ({min}) must be less than or equal to max ({max})");

        return new ClampFunction(input, min, max);
    }

    private RangeChoiceFunction CompileRangeChoice(JsonElement json, string path)
    {
        var input = this.Input(json, path, "input");
        var min = this.NoiseValue(json, path, "min_inclusive");
        var max = this.NoiseValue(json, path, "max_exclusive");
        var inRange = this.Input(json, path, "when_in_range");
        var outOfRange = this.Input(json, path, "when_out_of_range");

        return new RangeChoiceFunction(input, min, max, inRange, outOfRange);
    }

    private IntervalSelectFunction CompileIntervalSelect(JsonElement json, string path)
    {
        var input = this.Input(json, path, "input");

        var thresholdsPath = Field(path, "thresholds");
        var thresholds = Array(Required(json, path, "thresholds"), thresholdsPath)
            .Select((threshold, i) => Float(threshold, Index(thresholdsPath, i), -MaxNoiseValue, MaxNoiseValue))
            .ToImmutableArray();

        var functionsPath = Field(path, "functions");
        var functions = Array(Required(json, path, "functions"), functionsPath)
            .Select((function, i) => this.Compile(function, Index(functionsPath, i)))
            .ToImmutableArray();

        if (thresholds.IsEmpty || functions.Length != thresholds.Length + 1)
            throw Error(path, "needs at least one threshold and one function more than thresholds");

        for (var i = 1; i < thresholds.Length; i++)
        {
            if (thresholds[i] < thresholds[i - 1])
                throw Error(thresholdsPath, "Threshold values must be ordered from smallest to largest");
        }

        return new IntervalSelectFunction(input, thresholds, functions);
    }

    private InterpolatedFunction CompileInterpolated(JsonElement json, string path)
    {
        var input = this.Input(json, path, "input");
        var cellSizeXz = Int(Required(json, path, "cell_size_xz"), Field(path, "cell_size_xz"), 1, int.MaxValue);
        var cellSizeY = Int(Required(json, path, "cell_size_y"), Field(path, "cell_size_y"), 1, int.MaxValue);

        return new InterpolatedFunction(input, cellSizeXz, cellSizeY);
    }

    private SliceFunction CompileSlice(JsonElement json, string path)
    {
        var axis = Enum(json, path, "axis", null, Axes);
        var coordinate = Int(Required(json, path, "coordinate"), Field(path, "coordinate"));
        var input = this.Input(json, path, "input");

        return SliceFunction.Create(axis, coordinate, input);
    }

    private FindTopSurfaceFunction CompileFindTopSurface(JsonElement json, string path)
    {
        var density = this.Input(json, path, "density");
        var upperBound = this.Input(json, path, "upper_bound");
        var lowerBoundPath = Field(path, "lower_bound");
        var lowerBound = Int(Required(json, path, "lower_bound"), lowerBoundPath, MinFunctionY, MaxFunctionY);
        var cellHeight = Int(Required(json, path, "cell_height"), Field(path, "cell_height"), 1, int.MaxValue);

        return new FindTopSurfaceFunction(density, upperBound, lowerBound, cellHeight);
    }

    private CubicSpline CompileSpline(JsonElement json, string path)
    {
        if (json.ValueKind == JsonValueKind.Number)
            return new ConstantSpline(Float(json, path));

        var coordinate = this.Input(json, path, "coordinate");
        var pointsPath = Field(path, "points");
        var points = Array(Required(json, path, "points"), pointsPath).ToList();

        if (points.Count == 0)
            throw Error(pointsPath, "can't be empty");

        var locations = ImmutableArray.CreateBuilder<float>(points.Count);
        var values = ImmutableArray.CreateBuilder<CubicSpline>(points.Count);
        var derivatives = ImmutableArray.CreateBuilder<float>(points.Count);

        for (var i = 0; i < points.Count; i++)
        {
            var pointPath = Index(pointsPath, i);
            var point = points[i];

            locations.Add(Float(Required(point, pointPath, "location"), Field(pointPath, "location")));
            values.Add(this.CompileSpline(Required(point, pointPath, "value"), Field(pointPath, "value")));
            derivatives.Add(Float(Required(point, pointPath, "derivative"), Field(pointPath, "derivative")));
        }

        return new MultipointSpline(coordinate, locations.MoveToImmutable(), values.MoveToImmutable(),
            derivatives.MoveToImmutable());
    }

    private static T Enum<T>(JsonElement json, string path, string field, T? fallback, Dictionary<string, T> names)
        where T : struct
    {
        if (!json.TryGetProperty(field, out var value))
            return fallback ?? throw Error(path, $"missing {field}");

        var name = String(value, Field(path, field));
        return names.TryGetValue(name, out var result) ? result : throw Error(Field(path, field), $"unknown {name}");
    }

    private static readonly Dictionary<string, DensityAxis> Axes = new()
    {
        ["x"] = DensityAxis.X,
        ["y"] = DensityAxis.Y,
        ["z"] = DensityAxis.Z
    };

    private static readonly Dictionary<string, GradientTiling> Tilings = new()
    {
        ["clamp_to_edge"] = GradientTiling.ClampToEdge,
        ["repeat"] = GradientTiling.Repeat,
        ["mirrored_repeat"] = GradientTiling.MirroredRepeat
    };

    private static readonly Dictionary<string, DistanceMetric> DistanceMetrics = new()
    {
        ["euclidean"] = DistanceMetric.Euclidean,
        ["euclidean_squared"] = DistanceMetric.EuclideanSquared,
        ["manhattan"] = DistanceMetric.Manhattan,
        ["chebyshev"] = DistanceMetric.Chebyshev
    };
}
