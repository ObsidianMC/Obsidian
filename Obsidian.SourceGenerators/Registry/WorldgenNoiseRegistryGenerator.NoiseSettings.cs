using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenNoiseRegistryGenerator
{
    private static readonly string[] numbers = ["Int32", "Single", "Double", "Int64"];

    private const string defaultBlock = "default_block";
    private const string defaultFluid = "default_fluid";

    private static void BuildNoiseSettings(Dictionary<string, TypeInformation> densityFunctionTypes, Noises noises,
        CodeBuilder builder)
    {
        var settings = noises.Settings;

        builder.Type("public static class NoiseSettings");

        foreach (var setting in settings)
        {
            var sanitizedName = setting.Name.ToPascalCase();
            builder.Type($"public static readonly NoiseSetting {sanitizedName} = new()");

            builder.Line($"Identifier = \"{setting.Name}\", ");

            foreach (var property in setting.Properties)
            {
                var elementName = property.Name;
                var element = property.Value;

                //TODO ARRAY OBJECTS
                AppendChildProperty(densityFunctionTypes, elementName, element, builder, true);
            }

            builder.EndScope(true);
        }

        builder.Type("public static readonly FrozenDictionary<string, NoiseSetting> All = new Dictionary<string, NoiseSetting>()");

        foreach (var setting in settings)
        {
            var sanitizedName = setting.Name.ToPascalCase();

            builder.Line($"{{ \"{setting.Name}\", {sanitizedName}}}, ");
        }

        builder.EndScope(".ToFrozenDictionary()", true);

        builder.EndScope();
    }

    private static void AppendChildProperty(Dictionary<string, TypeInformation> densityFunctionTypes, string elementName,
        JsonElement element, CodeBuilder builder, bool newLine = false, bool isDensityFunction = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.AppendSimple($"{elementName.ToPascalCase()} = \"{element.GetString()}\", ", newLine);
                break;
            case JsonValueKind.Number:
                if (isDensityFunction)
                    builder.AppendSimple($"{elementName.ToPascalCase()} = new ConstantDensityFunction {{ Argument = ", newLine);
                else
                    builder.AppendSimple($"{elementName.ToPascalCase()} = ", newLine);

                AppendUnknownNumber(builder, element, newLine: false);
                builder.Append("}, ");
                break;
            case JsonValueKind.Array:
                builder.AppendSimple($"{elementName.ToPascalCase()} = [], ", newLine);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetBoolean().ToString().ToLower()}, ", newLine);
                break;
            default:
                {
                    if (TryAppendTypeProperty(densityFunctionTypes, elementName, element, builder, newLine))
                        break;

                    if (TryAppendStateProperty(elementName, element, builder))
                        break;

                    builder.AppendSimple($"{elementName.ToPascalCase()} = new() {{ ", newLine);

                    foreach (var childProperty in element.EnumerateObject())
                    {
                        var childName = childProperty.Name;
                        var childValue = childProperty.Value;

                        AppendChildProperty(densityFunctionTypes, childName, childValue, builder);
                    }

                    builder.Append("}, ");

                    break;
                }
        }
    }

    private static bool TryAppendTypeProperty(Dictionary<string, TypeInformation> densityFunctionTypes, string? elementName,
        JsonElement element, CodeBuilder builder, bool newLine = false)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (element.TryGetProperty("type", out var typeElement))
        {
            var typeName = typeElement.GetString()!;

            var value = densityFunctionTypes.GetValue(typeName);
            if (value is not TypeInformation featureType)
                return false;

            var name = elementName != null ? $"{elementName.ToPascalCase()} = new {featureType.Symbol.Name}() {{" :
                string.Empty;

            builder.Indent().Append(name);

            foreach (var childProperty in element.EnumerateObject().Where(x => x.Name != "type"))
            {
                var childName = childProperty.Name;
                var childValue = childProperty.Value;

                AppendChildProperty(densityFunctionTypes, childName, childValue, builder, true);
            }

            builder.Append("}, ");

            if (newLine)
                builder.Line();

            return true;
        }

        return false;
    }

    private static bool TryAppendStateProperty(string elementName, JsonElement element, CodeBuilder builder)
    {
        var isState = elementName is defaultBlock or defaultFluid;

        if (isState)
        {
            builder.Append($"{elementName.ToPascalCase()} = new() {{ ");

            builder.Append($"Name = \"{element.GetProperty("Name")}\", ");

            if (element.TryGetProperty("Properties", out var props))
            {
                builder.Append("Properties = new() { ");

                foreach (var prop in props.EnumerateObject())
                {
                    var childName = prop.Name;
                    var childValue = prop.Value;

                    builder.Append($" {{ \"{childName}\", \"{childValue.GetString()}\" }}, ");
                }

                builder.Append("}");
            }

            builder.Append("}, ");
        }

        return isState;
    }

    private static void AppendNumber(CodeBuilder builder, string elementName, JsonElement element, string numberType = "Int32", bool newLine = true)
    {
        if (numberType == "Int16")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetInt16()},", newLine);
        else if (numberType == "Int32")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetInt32()},", newLine);
        else if (numberType == "Int64")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetInt64()},", newLine);
        else if (numberType == "Single")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetSingle()}f,", newLine);
        else if (numberType == "Double")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetDouble()}d,", newLine);
    }

    private static void AppendNumber(CodeBuilder builder, JsonElement element, string numberType = "Int32", bool newLine = true)
    {
        if (numberType == "Int16")
            builder.AppendSimple($"{element.GetInt16()},", newLine);
        else if (numberType == "Int32")
            builder.AppendSimple($"{element.GetInt32()},", newLine);
        else if (numberType == "Int64")
            builder.AppendSimple($"{element.GetInt64()},", newLine);
        else if (numberType == "Single")
            builder.AppendSimple($"{element.GetSingle()}f,", newLine);
        else if (numberType == "Double")
            builder.AppendSimple($"{element.GetDouble()}d,", newLine);
    }

    private static void AppendUnknownNumber(CodeBuilder builder, JsonElement element, bool newLine = true)
    {
        if (element.TryGetInt16(out var shortValue))
            builder.AppendSimple($"{shortValue},", newLine);
        else if (element.TryGetInt32(out var intValue))
            builder.AppendSimple($"{intValue},", newLine);
        else if (element.TryGetInt64(out var longValue))
            builder.AppendSimple($"{longValue},", newLine);
        else if (element.TryGetSingle(out var floatValue))
            builder.AppendSimple($"{floatValue}f,", newLine);
        else if (element.TryGetDouble(out var doubleValue))
            builder.AppendSimple($"{doubleValue}d,", newLine);
    }
}
