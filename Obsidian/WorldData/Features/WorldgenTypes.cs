using System.Reflection;

namespace Obsidian.WorldData.Features;

/// <summary>
/// The worldgen types data files name by their <c>type</c> (or <c>predicate_type</c>, <c>processor_type</c>,
/// <c>element_type</c>): classes marked with <see cref="ConfiguredFeatureClassAttribute"/>,
/// <see cref="ConfiguredFeaturePropertyAttribute"/>, <see cref="TreePropertyAttribute"/>,
/// <see cref="ConfiguredFeatureAttribute"/> or <see cref="StructureTypeAttribute"/>, like the source generator finds.
/// </summary>
/// <remarks>
/// An identifier can name several types in different hierarchies (<c>minecraft:constant</c> is an int, float and height
/// provider), so a type is looked up by identifier and the base type the value is assigned to.
/// </remarks>
internal sealed class WorldgenTypes
{
    private readonly Dictionary<string, List<Type>> types = [];

    /// <summary>
    /// The types of Obsidian and Obsidian.API.
    /// </summary>
    public static WorldgenTypes BuiltIn { get; } = new(BuiltInAssemblies);

    private static Assembly[] BuiltInAssemblies => [typeof(WorldgenTypes).Assembly, typeof(IFeature).Assembly];

    private WorldgenTypes(IEnumerable<Assembly> assemblies) => this.Add(assemblies);

    /// <summary>
    /// The built-in types followed by the types of other assemblies, like plugins'. A built-in type wins over a
    /// plugin's with the same identifier and base type.
    /// </summary>
    public static WorldgenTypes With(IEnumerable<Assembly> assemblies)
    {
        var extra = assemblies.Except(BuiltInAssemblies).ToArray();
        if (extra.Length == 0)
            return BuiltIn;

        var result = new WorldgenTypes(BuiltInAssemblies);
        result.Add(extra);
        return result;
    }

    /// <summary>
    /// The first concrete type with the identifier that can be assigned to <paramref name="target"/>, or null.
    /// </summary>
    public Type? Find(string identifier, Type target) =>
        this.types.TryGetValue(identifier, out var candidates)
            ? candidates.FirstOrDefault(candidate => !candidate.IsAbstract && candidate.IsAssignableTo(target))
            : null;

    private void Add(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in LoadableTypes(assembly))
            {
                if (!type.IsClass)
                    continue;

                foreach (var identifier in Identifiers(type))
                {
                    if (!this.types.TryGetValue(identifier, out var list))
                        this.types[identifier] = list = [];

                    if (!list.Contains(type))
                        list.Add(type);
                }
            }
        }
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

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
