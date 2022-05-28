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

    private static Dictionary<string, string[]> enumValuesCache = new();

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
        if (!Customizations.RenamedEnums.TryGetValue(tag, out string type))
            type = name;

        if (Customizations.BannedNames.Contains(type))
            type = $"E{type}";

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
