using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.SurfaceRules;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>
/// Material rule and condition types beyond vanilla's that <see cref="MaterialRules"/> can compile, such as plugins'.
/// </summary>
internal interface IMaterialTypes
{
    /// <summary>
    /// Reads a rule of a type vanilla doesn't have, or gives null when the type isn't one of these.
    /// </summary>
    /// <param name="type">The object's <c>type</c>, as written.</param>
    /// <param name="rules">The generator's material rules, which compile the rule's own rules and conditions.</param>
    /// <exception cref="InvalidDataException">The object isn't a valid rule of the type.</exception>
    public ICustomSurfaceRule? ReadRule(string type, JsonElement json, MaterialRules rules);

    /// <summary>
    /// Reads a condition of a type vanilla doesn't have, or gives null when the type isn't one of these.
    /// </summary>
    /// <param name="type">The object's <c>type</c>, as written.</param>
    /// <param name="rules">The generator's material rules, which compile the condition's own rules and conditions.
    /// </param>
    /// <exception cref="InvalidDataException">The object isn't a valid condition of the type.</exception>
    public ICustomSurfaceCondition? ReadCondition(string type, JsonElement json, MaterialRules rules);
}

/// <summary>
/// The surface rule and condition types plugins registered (<c>IPluginRegistry.RegisterWorldgenType</c> with
/// <see cref="ISurfaceRule"/> or <see cref="ISurfaceCondition"/>), read for 26.3 material rules: the plugin's object
/// is read like its other worldgen types, its own rules, conditions and density functions are compiled by the 26.3
/// pipeline, and evaluating it calls the plugin's code.
/// </summary>
/// <remarks>
/// <para>
/// The rules and conditions a plugin's object holds are compiled 26.3 ones, which its context evaluates
/// (<see cref="ISurfaceContext.Apply"/>, <see cref="ISurfaceContext.Test"/>); its density functions are seeded 26.3
/// ones, as a plugin's density function's are (see <see cref="PluginDensityTypes"/>), so it can't hold a noise. Vanilla's
/// rule and condition classes it holds or makes, such as a <see cref="BlockSurfaceRule"/>, evaluate as 26.3's (see
/// <see cref="MaterialRules"/>' translations).
/// </para>
/// <para>
/// The plugins whose code the rules run are collected in <see cref="Owners"/>, which the dimension holds while it
/// generates (see <see cref="Biomes.WorldgenDimension"/>).
/// </para>
/// </remarks>
/// <param name="types">The worldgen types the loaded plugins registered.</param>
internal sealed class PluginMaterialTypes(WorldgenTypes types) : IMaterialTypes
{
    private readonly ConcurrentDictionary<PluginContainer, byte> owners = new();

    // Every type the rules read so far created, plugins' and Obsidian's.
    private readonly ConcurrentDictionary<Type, byte> created = new();

    /// <summary>
    /// The plugins whose types the rules and conditions read so far are made of.
    /// </summary>
    public ImmutableArray<PluginContainer> Owners => [.. this.owners.Keys];

    public ICustomSurfaceRule? ReadRule(string type, JsonElement json, MaterialRules rules) =>
        this.Read<ISurfaceRule>(type, json, rules) as ICustomSurfaceRule;

    public ICustomSurfaceCondition? ReadCondition(string type, JsonElement json, MaterialRules rules) =>
        this.Read<ISurfaceCondition>(type, json, rules) as ICustomSurfaceCondition;

    // Registering checks that a rule type implements ICustomSurfaceRule, and a condition type ICustomSurfaceCondition.
    private T? Read<T>(string type, JsonElement json, MaterialRules rules)
        where T : class
    {
        var identifier = WorldgenData.Qualify(type);
        if (types.FindRegistered(identifier, typeof(T)) is null)
            return null;

        var compiler = rules.Compiler;
        var reader = new WorldgenReader(
            types,
            NoFeature<ConfiguredFeatureBase>,
            NoFeature<PlacedFeature>,
            this.OnCreated)
        {
            DensityFunctions = (value, path) => new CompiledDensityFunction(compiler.Compile(value, path)),
            SurfaceRules = (value, _) => new CompiledSurfaceRule(rules.CompileRule(value)),
            SurfaceConditions = (value, _) => new CompiledSurfaceCondition(rules.CompileCondition(value)),
            Noises = NoNoise
        };

        return reader.ReadTyped<T>(json, identifier);
    }

    /// <summary>
    /// The types the rules read so far are made of, without the other types plugins registered, or null when they're
    /// made of none: what a dimension keeps once it's read, so that it only keeps its own plugins' code loaded. It
    /// reads the same rules from the same data.
    /// </summary>
    public PluginMaterialTypes? Narrowed()
    {
        var used = types.Narrow(this.created.Keys.ToHashSet());
        if (used == WorldgenTypes.BuiltIn)
            return null;

        var narrowed = new PluginMaterialTypes(used);

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

    // Named and inline noises alike: a 1.21.11 noise would sample unseeded here.
    private static BaseNoise NoNoise(JsonElement json, Type target, string path) =>
        throw new InvalidDataException(
            $"{path}: a plugin's surface rule or condition can't hold a noise in 26.3; take a density function, such "
            + "as a minecraft:noise, or a minecraft:noise_threshold condition, instead");
}

/// <summary>
/// A 26.3 material rule a plugin's rule or condition holds, which its context evaluates.
/// </summary>
/// <param name="rule">The compiled rule.</param>
internal sealed class CompiledSurfaceRule(Func<MaterialRules.MaterialContext, IBlock?> rule) : ISurfaceRule
{
    public string Type => "obsidian:compiled";

    /// <summary>The compiled rule.</summary>
    public Func<MaterialRules.MaterialContext, IBlock?> Rule { get; } = rule;
}

/// <summary>
/// A 26.3 material condition a plugin's rule or condition holds, which its context tests.
/// </summary>
/// <param name="condition">The compiled condition.</param>
internal sealed class CompiledSurfaceCondition(Func<MaterialRules.MaterialContext, bool> condition)
    : ISurfaceCondition
{
    public string Type => "obsidian:compiled";

    /// <summary>The compiled condition.</summary>
    public Func<MaterialRules.MaterialContext, bool> Condition { get; } = condition;
}
