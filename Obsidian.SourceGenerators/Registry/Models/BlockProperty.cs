using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal class BlockProperty
{
    public string Name { get; }
    public string Tag { get; }
    public string Type { get; }
    public string[] Values { get; }
    public bool IsEnum { get; }
    public int? CustomOffset { get; }
    public bool IsBooleanToggled { get; }

    private const string BooleanName = "bool";
    private const string IntegerName = "int";

    internal static Dictionary<string, string[]> enumValuesCache = [];

    private BlockProperty(string name, string tag, string type, string[] values, int? customOffset = null, bool isBooleanToggled = true)
    {
        Name = name;
        Tag = tag;
        Type = type;
        Values = values;
        CustomOffset = customOffset;
        IsBooleanToggled = isBooleanToggled;
        IsEnum = type != BooleanName && type != IntegerName;
    }

    public static BlockProperty Get(JsonProperty property)
    {
        string name = property.Name.RemoveNamespace().ToPascalCase();

        string[] values = property.Value
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray();

        // Boolean
        if (values is { Length: 2 } && values[0] is "true" or "false")
        {
            return new BlockProperty($"Is{name}", property.Name, BooleanName, values);
        }

        // Integer
        if (values.All(text => int.TryParse(text, out _)))
        {
            return new BlockProperty(name, property.Name, IntegerName, values);
        }

        // Enum
        string tag = Customizations.GetEnumTag(values);
        string type = EnumTypeName(property.Name, values);

        if (enumValuesCache.TryGetValue(type, out var cachedValues))
        { 
            values = cachedValues;
        }
        else
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = values[i].ToPascalCase();
            }

            enumValuesCache.Add(type, values);
        }

        return new BlockProperty(name, tag, type, values);
    }

    /// <summary>
    /// The enum a block state property is generated as: <see cref="Customizations.RenamedEnums"/> by its values, else
    /// the property's name, prefixed with E when banned. Other generators use it to know the enums
    /// <see cref="BlocksGenerator"/> emits into Obsidian.API, whose output they can't see.
    /// </summary>
    internal static string EnumTypeName(string propertyName, string[] values)
    {
        if (!Customizations.RenamedEnums.TryGetValue(Customizations.GetEnumTag(values), out string type))
            type = propertyName.RemoveNamespace().ToPascalCase();

        return Customizations.BannedNames.Contains(type) ? $"E{type}" : type;
    }

    /// <summary>
    /// The enums <see cref="BlocksGenerator"/> emits into Obsidian.API from <c>blocks.json</c>, by name, with their
    /// members in order (the first property of each name decides its members).
    /// </summary>
    internal static Dictionary<string, string[]> EnumsOf(string blocksJson)
    {
        var enums = new Dictionary<string, string[]>();
        using var document = JsonDocument.Parse(blocksJson);
        foreach (var block in document.RootElement.EnumerateObject())
        {
            if (!block.Value.TryGetProperty("properties", out var properties))
                continue;

            foreach (var property in properties.EnumerateObject())
            {
                var values = property.Value.EnumerateArray().Select(element => element.GetString()!).ToArray();
                if (values is { Length: 2 } && values[0] is "true" or "false" || values.All(text => int.TryParse(text, out _)))
                    continue;

                var type = EnumTypeName(property.Name, values);
                if (type != "BlockFace" && !enums.ContainsKey(type))
                    enums[type] = [.. values.Select(value => value.ToPascalCase())];
            }
        }

        return enums;
    }

    public string GetValueFormula(ref int offset)
    {
        int multiplier = CustomOffset ?? offset;
        offset *= Values.Length;
        return Type switch
        {
            BooleanName when IsBooleanToggled => $"({Name} ? 0 : {multiplier})",
            BooleanName when !IsBooleanToggled => $"({Name} ? {multiplier} : 0)",
            IntegerName => $"({Name} * {multiplier})",
            _ => $"((int){Name} * {multiplier})",
        };
    }
}
