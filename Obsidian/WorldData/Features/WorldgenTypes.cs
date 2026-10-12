using System.Reflection;

namespace Obsidian.WorldData.Features;

/// <summary>
/// The worldgen types data files name by their <c>type</c> (or <c>predicate_type</c>, <c>processor_type</c>,
/// <c>element_type</c>): Obsidian's classes marked with <see cref="ConfiguredFeatureClassAttribute"/>,
/// <see cref="ConfiguredFeaturePropertyAttribute"/>, <see cref="TreePropertyAttribute"/>,
/// <see cref="ConfiguredFeatureAttribute"/> or <see cref="StructureTypeAttribute"/>, like the source generator finds,
/// and the types plugins registered.
/// </summary>
/// <remarks>
/// An identifier can name several types in different hierarchies (<c>minecraft:constant</c> is an int, float and height
/// provider), so a type is looked up by identifier and the base type the value is assigned to.
/// </remarks>
internal sealed class WorldgenTypes
{
    private readonly Dictionary<string, List<Type>> builtIn;
    private readonly Dictionary<string, List<WorldgenTypeRegistration>> registered = [];

    /// <summary>
    /// The types of Obsidian and Obsidian.API.
    /// </summary>
    public static WorldgenTypes BuiltIn { get; } =
        new(Scan([typeof(WorldgenTypes).Assembly, typeof(IFeature).Assembly]));

    private WorldgenTypes(Dictionary<string, List<Type>> builtIn) => this.builtIn = builtIn;

    /// <summary>
    /// The built-in types and the types plugins registered. Plugins' assemblies aren't scanned: their types are only
    /// found when registered, so attributes on them don't count.
    /// </summary>
    public static WorldgenTypes With(IEnumerable<WorldgenTypeRegistration> registrations)
    {
        // Shares the built-in map, which is never changed once scanned.
        var result = new WorldgenTypes(BuiltIn.builtIn);

        foreach (var registration in registrations)
        {
            if (!result.registered.TryGetValue(registration.Identifier, out var list))
                result.registered[registration.Identifier] = list = [];

            list.Add(registration);
        }

        return result.registered.Count == 0 ? BuiltIn : result;
    }

    /// <summary>
    /// The first concrete type with the identifier that can be assigned to <paramref name="target"/>, or null. A
    /// registered type is only found where its registered base type is expected.
    /// </summary>
    public Type? Find(string identifier, Type target)
    {
        if (this.builtIn.TryGetValue(identifier, out var candidates)
            && candidates.FirstOrDefault(candidate => !candidate.IsAbstract && candidate.IsAssignableTo(target))
                is Type type)
        {
            return type;
        }

        return this.registered.TryGetValue(identifier, out var registrations)
            ? registrations.FirstOrDefault(registration => registration.Base.IsAssignableTo(target))?.Type
            : null;
    }

    private static Dictionary<string, List<Type>> Scan(IEnumerable<Assembly> assemblies)
    {
        var types = new Dictionary<string, List<Type>>();

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()))
        {
            if (!type.IsClass)
                continue;

            foreach (var identifier in Identifiers(type))
            {
                if (!types.TryGetValue(identifier, out var list))
                    types[identifier] = list = [];

                if (!list.Contains(type))
                    list.Add(type);
            }
        }

        return types;
    }

    private static IEnumerable<string> Identifiers(Type type)
    {
        foreach (var attribute in type.GetCustomAttributes(inherit: false))
        {
            var identifier = attribute switch
            {
                ConfiguredFeatureClassAttribute feature => feature.Identifier,
                ConfiguredFeaturePropertyAttribute property => property.ResourceLocation,
                TreePropertyAttribute tree => tree.ResourceLocation,
                ConfiguredFeatureAttribute feature => feature.ResourceLocation,
                StructureTypeAttribute structure => structure.Identifier,
                _ => null
            };

            if (identifier is not null)
                yield return identifier;
        }
    }
}

/// <summary>
/// A worldgen type a plugin registered: data names <see cref="Type"/> by <see cref="Identifier"/> where a
/// <see cref="Base"/> (a feature, a placement modifier, an int provider, ...) is expected.
/// </summary>
internal sealed record WorldgenTypeRegistration(string Identifier, Type Base, Type Type)
{
    /// <summary>
    /// Checks a registration against the rules and the plugin's earlier ones.
    /// </summary>
    /// <param name="registered">The plugin's registrations so far.</param>
    /// <exception cref="ArgumentException">The identifier is vanilla's, or already names a type of the same base;
    /// the base can't be chosen by a type name; or the type can't be created.</exception>
    public static WorldgenTypeRegistration Create(
        IEnumerable<WorldgenTypeRegistration> registered,
        string identifier,
        Type baseType,
        Type type)
    {
        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new ArgumentException($"{identifier} is vanilla's; plugins' types are in their own namespace.");

        if (!baseType.IsInterface && !baseType.IsAbstract)
            throw new ArgumentException($"{baseType.Name} isn't an abstract class or interface data can choose from.");

        // The reader creates values with a parameterless constructor, public or not, and then sets their properties.
        if (type.IsAbstract
            || type.ContainsGenericParameters
            || type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [])
                is null)
        {
            throw new ArgumentException($"{type.Name} needs to be a concrete class with a parameterless constructor.");
        }

        if (registered.Any(other => other.Identifier == identifier && other.Base == baseType))
            throw new ArgumentException($"{identifier} is already registered as a {baseType.Name} type.");

        return new WorldgenTypeRegistration(identifier, baseType, type);
    }
}
