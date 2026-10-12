using Obsidian.WorldData.Structures;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Reads configured and placed features from data pack JSON at runtime, into the same objects Obsidian's source
/// generator makes of vanilla's features (<c>WorldgenFeatureRegistryGenerator</c>).
/// </summary>
/// <remarks>
/// A value is read according to the type of the property it's assigned to:
/// <list type="bullet">
/// <item>JSON keys map to settable properties (<c>block_state</c> to <c>BlockState</c>);</item>
/// <item>an abstract or interface type is chosen by the object's <c>type</c>, <c>predicate_type</c>,
/// <c>processor_type</c> or <c>element_type</c> among <see cref="WorldgenTypes"/>; a number is its
/// <c>minecraft:constant</c> and a bare vertical anchor a constant height provider;</item>
/// <item>a single value given for a list is a list of it, and enums are matched ignoring case and underscores;</item>
/// <item>configured features, placed features and processor lists given by identifier are looked up, others are
/// read inline.</item>
/// </list>
/// What the generator wouldn't compile is an error here, thrown as an <see cref="InvalidDataException"/> naming the
/// field: an unknown type, key or enum name, a required property missing, or a value of the wrong kind.
/// </remarks>
/// <param name="types">The types data can name.</param>
/// <param name="configuredFeature">Looks up a configured feature by identifier, or throws an
/// <see cref="InvalidDataException"/>.</param>
/// <param name="placedFeature">Looks up a placed feature by identifier, or throws an
/// <see cref="InvalidDataException"/>.</param>
/// <param name="created">Called with the type of every object created, such as a plugin's feature type.</param>
internal sealed class WorldgenReader(
    WorldgenTypes types,
    Func<string, ConfiguredFeatureBase> configuredFeature,
    Func<string, PlacedFeature> placedFeature,
    Action<Type>? created = null)
{
    private static readonly string[] discriminators = ["type", "predicate_type", "processor_type", "element_type"];

    private static readonly MethodInfo asImmutableArray = typeof(ImmutableCollectionsMarshal)
        .GetMethod(nameof(ImmutableCollectionsMarshal.AsImmutableArray))!;

    /// <summary>
    /// Reads a configured feature, <c>{"type": ..., "config": {...}}</c>.
    /// </summary>
    /// <param name="identifier">The feature's identifier, or null for one defined inline.</param>
    /// <param name="path">Where the value is in its file, for messages.</param>
    public ConfiguredFeatureBase ReadConfiguredFeature(JsonElement json, string? identifier = null, string path = "") =>
        Guard(path, () => this.ReadConfiguredFeature(json, typeof(ConfiguredFeatureBase), identifier, path));

    /// <summary>
    /// Reads a placed feature, <c>{"feature": ..., "placement": [...]}</c>.
    /// </summary>
    /// <param name="identifier">The feature's identifier, or null for one defined inline.</param>
    /// <param name="path">Where the value is in its file, for messages.</param>
    public PlacedFeature ReadPlacedFeature(JsonElement json, string? identifier = null, string path = "") =>
        Guard(path, () => this.ReadPlacedFeatureCore(json, identifier, path));

    // Reading reflects over types, plugins' included, and runs their constructors and setters, which can fail in
    // more ways than the data's errors (a plugin type missing a dependency, an open generic type, ...). Any failure
    // is the file's, so that the file is left out rather than stopping the plugin loading.
    private static T Guard<T>(string path, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not (InvalidDataException or OutOfMemoryException))
        {
            throw Error(path, ex.InnerException?.Message ?? ex.Message);
        }
    }

    private PlacedFeature ReadPlacedFeatureCore(JsonElement json, string? identifier, string path)
    {
        Object(json, path);

        var feature = Required(json, path, "feature");
        var featurePath = Field(path, "feature");

        var placement = json.TryGetProperty("placement", out var modifiers)
            ? (ImmutableArray<PlacementModifierBase>)this.ReadValue(
                typeof(ImmutableArray<PlacementModifierBase>),
                modifiers,
                Field(path, "placement"))!
            : [];

        return new PlacedFeature
        {
            Identifier = identifier ?? string.Empty,
            Feature = feature.ValueKind == JsonValueKind.String
                ? this.Configured(feature, featurePath)
                : this.ReadConfiguredFeature(feature, typeof(ConfiguredFeatureBase), null, featurePath),
            Placement = placement
        };
    }

    private ConfiguredFeatureBase ReadConfiguredFeature(JsonElement json, Type target, string? identifier, string path)
    {
        Object(json, path);

        var name = Reference(Required(json, path, "type"), Field(path, "type"));
        var type = types.Find(name, target)
            ?? throw Error(Field(path, "type"), $"{name} isn't a feature type{Expecting(target)}");

        var config = json.TryGetProperty("config", out var value) ? value : default;
        return (ConfiguredFeatureBase)this.ReadObject(type, config, null, identifier, Field(path, "config"));
    }

    private object? ReadValue(Type target, JsonElement json, string path)
    {
        target = Nullable.GetUnderlyingType(target) ?? target;

        try
        {
            if (target == typeof(string))
                return json.ValueKind == JsonValueKind.String ? json.GetString() : json.GetRawText();

            if (target == typeof(bool))
                return json.GetBoolean();

            if (target == typeof(int))
                return json.GetInt32();

            if (target == typeof(long))
                return json.GetInt64();

            if (target == typeof(float))
                return json.GetSingle();

            if (target == typeof(double))
                return json.GetDouble();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw Error(path, $"expected a {target.Name.ToLowerInvariant()}");
        }

        if (target.IsEnum)
            return ReadEnum(target, json, path);

        if (CollectionElement(target) is Type element)
            return this.ReadCollection(target, element, json, path);

        if (target == typeof(SimpleBlockState))
            return ReadBlockState(json, path);

        if (target == typeof(BlockSet))
            return new BlockSet(ReadEntries(json, path, block: true));

        if (target == typeof(BiomeSet))
            return new BiomeSet(ReadEntries(json, path, block: false));

        if (target == typeof(Vector))
            return ReadVector(json, path);

        if (target == typeof(VerticalAnchor))
            return ReadVerticalAnchor(json, path);

        if (target == typeof(PlacedFeature))
        {
            return json.ValueKind == JsonValueKind.String
                ? this.Placed(json, path)
                : this.ReadPlacedFeatureCore(json, null, path);
        }

        if (target == typeof(StructureProcessorList))
            return this.ReadProcessorList(json, path);

        if (target.IsAssignableTo(typeof(ConfiguredFeatureBase)))
            return this.ReadConfiguredFeatureValue(target, json, path);

        if (target.IsAbstract || target.IsInterface)
            return this.ReadPolymorphic(target, json, path);

        return this.ReadObject(target, json, null, null, path);
    }

    private ConfiguredFeatureBase ReadConfiguredFeatureValue(Type target, JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.String)
            return this.ReadConfiguredFeature(json, target, null, path);

        var feature = this.Configured(json, path);
        return feature.GetType().IsAssignableTo(target)
            ? feature
            : throw Error(path, $"{feature.Identifier} is a {feature.Type} feature{Expecting(target)}");
    }

    // A processor list is an identifier, {"processors": [...]} or, as vanilla's codec allows, a bare list.
    private StructureProcessorList ReadProcessorList(JsonElement json, string path)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            var id = Reference(json, path);
            return ProcessorLists.All.TryGetValue(id, out var list)
                ? list
                : throw Error(path, $"{id} isn't a processor list");
        }

        if (json.ValueKind != JsonValueKind.Array)
            return (StructureProcessorList)this.ReadObject(typeof(StructureProcessorList), json, null, null, path);

        var processors = this.ReadValue(typeof(ImmutableArray<StructureProcessor>), json, path);
        return new StructureProcessorList { Processors = (ImmutableArray<StructureProcessor>)processors! };
    }

    private object ReadPolymorphic(Type target, JsonElement json, string path)
    {
        if (json.ValueKind == JsonValueKind.Number && types.Find("minecraft:constant", target) is Type constant)
        {
            var instance = Create(constant, path);
            var value = FindProperty(constant, "Value")!;
            Set(value, instance, this.ReadValue(value.PropertyType, json, path), path);
            return instance;
        }

        if (json.ValueKind == JsonValueKind.Object)
        {
            var discriminator = discriminators.FirstOrDefault(name => json.TryGetProperty(name, out _));

            // A height provider can be a bare vertical anchor, which is a constant one.
            if (discriminator is null && target == typeof(IHeightProvider))
            {
                var height = types.Find("minecraft:constant", target)!;
                var instance = Create(height, path);
                Set(FindProperty(height, "Value")!, instance, ReadVerticalAnchor(json, path), path);
                return instance;
            }

            if (discriminator is not null)
            {
                var name = Reference(json.GetProperty(discriminator), Field(path, discriminator));
                var type = types.Find(name, target)
                    ?? throw Error(Field(path, discriminator), $"{name} isn't a type{Expecting(target)}");

                return this.ReadObject(type, json, discriminator, null, path);
            }
        }

        throw Error(path, $"expected an object with a type{Expecting(target)}");
    }

    /// <summary>
    /// Creates <paramref name="type"/> and sets a property for each JSON key.
    /// </summary>
    /// <param name="discriminator">The key naming the type, which sets <c>Type</c> where it can be set.</param>
    /// <param name="identifier">The registry identifier to set, for a named entry.</param>
    private object ReadObject(Type type, JsonElement json, string? discriminator, string? identifier, string path)
    {
        var instance = Create(type, path);

        var assigned = new HashSet<string>();

        if (identifier is not null)
        {
            Set(FindProperty(type, "Identifier")!, instance, identifier, path);
            assigned.Add("Identifier");
        }

        if (discriminator is not null && FindProperty(type, "Type") is { SetMethod: not null } typeProperty)
        {
            Set(typeProperty, instance, json.GetProperty(discriminator).GetString(), path);
            assigned.Add("Type");
        }

        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                if (property.Name == discriminator)
                    continue;

                var name = PropertyName(property.Name);
                var member = FindProperty(type, name);
                var memberPath = Field(path, property.Name);

                if (member?.SetMethod is null)
                    throw Error(memberPath, $"{type.Name} has no such field");

                Set(member, instance, this.ReadValue(member.PropertyType, property.Value, memberPath), memberPath);
                assigned.Add(name);
            }
        }
        else if (json.ValueKind != JsonValueKind.Undefined)
        {
            throw Error(path, "expected an object");
        }

        // The generator's object initialisers wouldn't compile without them.
        foreach (var required in RequiredProperties(type))
        {
            if (!assigned.Contains(required.Name))
                throw Error(Field(path, required.Name.ToSnakeCase()), "is required");
        }

        return instance;
    }

    private object ReadCollection(Type target, Type element, JsonElement json, string path)
    {
        var items = json.ValueKind == JsonValueKind.Array ? json.GetArrayLength() : 1;
        var array = System.Array.CreateInstance(element, items);

        if (json.ValueKind == JsonValueKind.Array)
        {
            for (var i = 0; i < items; i++)
                array.SetValue(this.ReadValue(element, json[i], Index(path, i)), i);
        }
        else
        {
            array.SetValue(this.ReadValue(element, json, path), 0);
        }

        if (target.IsArray)
            return array;

        var definition = target.GetGenericTypeDefinition();
        if (definition == typeof(ImmutableArray<>))
            return asImmutableArray.MakeGenericMethod(element).Invoke(null, [array])!;

        if (definition == typeof(List<>) || definition == typeof(ICollection<>) || definition == typeof(IList<>))
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(element), array)!;

        return array;
    }

    // The element type of the collections the generator fills from JSON lists.
    private static Type? CollectionElement(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (!type.IsGenericType || type.GenericTypeArguments.Length != 1)
            return null;

        var definition = type.GetGenericTypeDefinition();
        var isCollection = definition == typeof(ImmutableArray<>)
            || definition == typeof(List<>)
            || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IEnumerable<>)
            || definition == typeof(IReadOnlyCollection<>)
            || definition == typeof(ICollection<>)
            || definition == typeof(IList<>);

        return isCollection ? type.GenericTypeArguments[0] : null;
    }

    private static object ReadEnum(Type type, JsonElement json, string path)
    {
        var text = String(json, path);
        var names = System.Enum.GetNames(type);
        var name = names.FirstOrDefault(name => Normalize(name) == Normalize(text))
            ?? throw Error(path, $"{text} isn't one of {string.Join(", ", names.Select(name => name.ToSnakeCase()))}");

        return System.Enum.Parse(type, name);
    }

    private static SimpleBlockState ReadBlockState(JsonElement json, string path)
    {
        Object(json, path);

        var name = Reference(Required(json, path, "Name"), Field(path, "Name"));
        if (!BlocksRegistry.TryGetDefault(name, out _))
            throw Error(Field(path, "Name"), $"{name} isn't a block");

        var properties = new Dictionary<string, string>();
        if (json.TryGetProperty("Properties", out var values))
        {
            foreach (var property in Object(values, Field(path, "Properties")).EnumerateObject())
                properties[property.Name] = String(property.Value, Field(Field(path, "Properties"), property.Name));
        }

        return new SimpleBlockState
        {
            Name = name,
            Properties = properties
        };
    }

    // A block or biome holder set: an identifier, a #tag, or a list of identifiers.
    private static string[] ReadEntries(JsonElement json, string path, bool block)
    {
        var entries = json.ValueKind == JsonValueKind.Array
            ? json.EnumerateArray().Select((entry, i) => String(entry, Index(path, i))).ToArray()
            : [String(json, path)];

        if (!block)
            return entries;

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.StartsWith('#'))
            {
                if (!TryParseIdentifier(entry[1..], out var tag) || !TagsRegistry.TryGet("block", tag, out _))
                    throw Error(path, $"{entry} isn't a block tag");

                entries[i] = $"#{tag}";
                continue;
            }

            if (!TryParseIdentifier(entry, out var id) || !BlocksRegistry.TryGetDefault(id, out _))
                throw Error(path, $"{entry} isn't a block");

            entries[i] = id;
        }

        return entries;
    }

    private static Vector ReadVector(JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() != 3)
            throw Error(path, "expected a list of 3 ints");

        return new Vector(Int(json[0], Index(path, 0)), Int(json[1], Index(path, 1)), Int(json[2], Index(path, 2)));
    }

    private static VerticalAnchor ReadVerticalAnchor(JsonElement json, string path)
    {
        Object(json, path);

        if (json.TryGetProperty("absolute", out var absolute))
            return VerticalAnchor.WithAbsolute(Int(absolute, Field(path, "absolute")));

        if (json.TryGetProperty("above_bottom", out var aboveBottom))
            return VerticalAnchor.WithAboveBottom(Int(aboveBottom, Field(path, "above_bottom")));

        if (json.TryGetProperty("below_top", out var belowTop))
            return VerticalAnchor.WithBelowTop(Int(belowTop, Field(path, "below_top")));

        throw Error(path, "expected a vertical anchor: absolute, above_bottom or below_top");
    }

    private ConfiguredFeatureBase Configured(JsonElement json, string path) =>
        Lookup(configuredFeature, Reference(json, path), path);

    private PlacedFeature Placed(JsonElement json, string path) => Lookup(placedFeature, Reference(json, path), path);

    // Looks a reference up, with the referring field in the message when it can't be found or read.
    private static T Lookup<T>(Func<string, T> lookup, string identifier, string path)
    {
        try
        {
            return lookup(identifier);
        }
        catch (InvalidDataException ex)
        {
            throw Error(path, ex.Message);
        }
    }

    // An identifier, with minecraft: added when it has no namespace, like vanilla's ResourceLocation.
    private static string Reference(JsonElement json, string path) => Identifier(json, path);

    // Creating a type, and setting its properties, runs its code: a failure is the data's error, so that a
    // plugin's file is left out rather than stopping the plugin loading.
    private object Create(Type type, string path)
    {
        object instance;

        try
        {
            instance = Activator.CreateInstance(type, nonPublic: true)
                ?? throw Error(path, $"{type.Name} can't be created");
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException or MemberAccessException)
        {
            throw Error(path, $"{type.Name} can't be created: {ex.InnerException?.Message ?? ex.Message}");
        }

        created?.Invoke(type);
        return instance;
    }

    private static void Set(PropertyInfo property, object instance, object? value, string path)
    {
        try
        {
            property.SetValue(instance, value);
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException)
        {
            throw Error(path, ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetProperty(name, flags) is PropertyInfo property)
                return property;
        }

        return null;
    }

    private static IEnumerable<PropertyInfo> RequiredProperties(Type type) => type
        .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(property => property.IsDefined(typeof(RequiredMemberAttribute), inherit: false));

    private static string Expecting(Type target) => target == typeof(object) ? string.Empty : $" of {target.Name}";

    private static string Normalize(string value) => value.Replace("_", string.Empty).ToLowerInvariant();

    // A JSON key to a property name like the generator's: discard_chance_on_air_exposure to
    // DiscardChanceOnAirExposure, firstOctave to FirstOctave.
    private static string PropertyName(string key)
    {
        var builder = new StringBuilder(key.Length);
        foreach (var part in key.Split('_'))
        {
            if (part.Length > 0)
                builder.Append(char.ToUpperInvariant(part[0])).Append(part.AsSpan(1));
        }

        return builder.ToString();
    }
}
