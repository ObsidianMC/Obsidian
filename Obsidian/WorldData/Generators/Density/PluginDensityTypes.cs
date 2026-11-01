using Obsidian.API.World.Generator.Noise;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// Density function types beyond vanilla's that <see cref="DensityCompiler"/> can build, such as plugins'.
/// </summary>
internal interface IDensityTypes
{
    /// <summary>
    /// Builds a function of a type vanilla doesn't have, or gives null when the type isn't one of these.
    /// </summary>
    /// <param name="type">The object's <c>type</c>, as written.</param>
    /// <param name="path">Where the value is, for messages.</param>
    /// <param name="compiler">The world's compiler, which builds the function's own density functions.</param>
    /// <exception cref="InvalidDataException">The object isn't a valid function of the type.</exception>
    public DensityFunction? Compile(string type, JsonElement json, string path, DensityCompiler compiler);
}

/// <summary>
/// The density function types plugins registered (<c>IPluginRegistry.RegisterWorldgenType</c> with
/// <see cref="IDensityFunction"/>), built into 26.3 density functions: the plugin's object is read like its other
/// worldgen types, its own density functions are compiled by the 26.3 compiler, and sampling calls the plugin's code.
/// </summary>
/// <remarks>
/// <para>
/// The 1.21.11 pipeline gives plugins' functions 1.21.11 functions and noises, which it seeds through
/// <see cref="IDensityFunction.MapAll"/>. Here the functions a plugin's function holds are already seeded 26.3 ones,
/// so nothing is mapped. Its <see cref="INoise"/> and <see cref="BaseNoise"/> properties name 26.3 noises, which are
/// seeded for the compiler's world from their identifiers, as vanilla seeds the noises its own functions name (see
/// <see cref="SeededNoise"/>). Noises written inline aren't: vanilla doesn't seed those, and 1.21.11's have the old
/// schema. Nor is a property typed as the plugin's own noise class, which can't sample a 26.3 noise. Those are refused
/// rather than sampled unseeded.
/// </para>
/// <para>
/// The plugins whose code the functions run are collected in <see cref="Owners"/>, which the dimension holds while it
/// generates (see <see cref="Biomes.WorldgenDimension"/>).
/// </para>
/// </remarks>
/// <param name="types">The worldgen types the loaded plugins registered.</param>
internal sealed class PluginDensityTypes(WorldgenTypes types) : IDensityTypes
{
    private readonly ConcurrentDictionary<PluginContainer, byte> owners = new();

    // Every type the functions built so far created, plugins' and Obsidian's.
    private readonly ConcurrentDictionary<Type, byte> created = new();

    /// <summary>
    /// The plugins whose types the functions built so far are made of.
    /// </summary>
    public ImmutableArray<PluginContainer> Owners => [.. this.owners.Keys];

    public DensityFunction? Compile(string type, JsonElement json, string path, DensityCompiler compiler)
    {
        var identifier = WorldgenData.Qualify(type);
        if (types.FindRegistered(identifier, typeof(IDensityFunction)) is null)
            return null;

        var reader = new WorldgenReader(
            types,
            NoFeature<ConfiguredFeatureBase>,
            NoFeature<PlacedFeature>,
            this.OnCreated)
        {
            DensityFunctions = (value, valuePath) => new CompiledDensityFunction(compiler.Compile(value, valuePath)),
            Noises = (value, target, valuePath) => Noise(value, target, valuePath, compiler)
        };

        var function = reader.ReadTyped<IDensityFunction>(json, path);

        return new PluginDensityFunction(function);
    }

    /// <summary>
    /// The types the functions built so far are made of, without the other types plugins registered, or null when
    /// they're made of none: what a dimension keeps once it's read, so that it only keeps its own plugins' code
    /// loaded. It builds the same functions from the same data.
    /// </summary>
    public PluginDensityTypes? Narrowed()
    {
        var used = types.Narrow(this.created.Keys.ToHashSet());
        if (used == WorldgenTypes.BuiltIn)
            return null;

        var narrowed = new PluginDensityTypes(used);

        foreach (var owner in this.owners.Keys)
            narrowed.owners.TryAdd(owner, 0);

        return narrowed;
    }

    private void OnCreated(Type type)
    {
        this.created.TryAdd(type, 0);

        if (WorldgenDependencies<PluginContainer>.Owner(type) is PluginContainer owner)
            this.owners.TryAdd(owner, 0);
    }

    private static T NoFeature<T>(string identifier) =>
        throw new InvalidDataException($"{identifier}: features can't be used here");

    // A named 26.3 noise, seeded for the compiler's world; inline noises and a plugin's own noise class can't be.
    private static BaseNoise Noise(JsonElement json, Type target, string path, DensityCompiler compiler)
    {
        if (target != typeof(INoise) && target != typeof(BaseNoise))
        {
            throw new InvalidDataException(
                $"{path}: a plugin's density function can't hold a noise of its own class {target.Name} in 26.3, "
                + "which can't sample a 26.3 noise; type it INoise or BaseNoise and name a noise");
        }

        if (json.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"{path}: a plugin's density function can't hold an inline noise in 26.3, which vanilla doesn't seed; "
                + "name a noise instead");
        }

        var identifier = WorldgenData.Qualify(json.GetString()!);

        return new BaseNoise(identifier, new SeededNoise(compiler.Noise(identifier)));
    }
}

/// <summary>
/// A 26.3 noise seeded for a world, as the <see cref="INoise"/> plugins' types sample (see
/// <see cref="PluginDensityTypes"/>): its value at a point, like a <c>minecraft:noise</c> function of scale 1.
/// </summary>
/// <param name="noise">The noise, which <see cref="DensityCompiler.Noise"/> seeded.</param>
internal sealed class SeededNoise(NoiseStack noise) : INoise
{
    public string Type => "obsidian:seeded_noise";

    // A bound rather than the exact range, as vanilla bounds its noises: each gradient noise taken as at most 2.
    public double MaxValue { get; } = noise.Layers.Sum(layer => 2.0 * Math.Abs(layer.Amplitude));

    public double MinValue => -this.MaxValue;

    public void Create()
    {
    }

    public double GetValue(double x, double y, double z) => noise.Get(x, y, z);
}

/// <summary>
/// A plugin's density function in the 26.3 pipeline: its value narrowed to float, as vanilla's functions are.
/// </summary>
/// <param name="function">The plugin's function, whose own functions are 26.3 ones.</param>
internal sealed class PluginDensityFunction(IDensityFunction function) : DensityFunction
{
    /// <summary>The plugin's function.</summary>
    public IDensityFunction Function { get; } = function;

    public override float Sample(int x, int y, int z) => (float)this.Function.GetValue(x, y, z);
}

/// <summary>
/// A 26.3 density function a plugin's function holds, as the interface plugins' functions sample.
/// </summary>
/// <remarks>
/// 26.3's functions don't track their ranges, so the range is unknown: from negative to positive infinity.
/// </remarks>
/// <param name="function">The compiled function.</param>
internal sealed class CompiledDensityFunction(DensityFunction function) : IDensityFunction
{
    public string Type => "obsidian:compiled";

    /// <summary>The compiled function.</summary>
    public DensityFunction Function { get; } = function;

    public double MinValue => double.NegativeInfinity;

    public double MaxValue => double.PositiveInfinity;

    // Plugins sample at block positions, which are whole.
    public double GetValue(double x, double y, double z) =>
        this.Function.Sample((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
}
