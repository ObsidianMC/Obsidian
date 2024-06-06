using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenNoiseRegistryGenerator
{
    private static void AppendChildProperty(CleanedNoises cleanedNoises, string elementName,
        JsonElement element, CodeBuilder builder, bool newLine = false, bool isDensityFunction = false, TypeInformation? densityFunction = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (TryAppendTypeProperty(cleanedNoises, elementName, element, builder, newLine))
                    break;
                if (elementName == "type" && isDensityFunction)
                    break;

                builder.AppendSimple($"{elementName.ToPascalCase()} = \"{element.GetString()}\", ", newLine);
                break;
            case JsonValueKind.Number:
                if (isDensityFunction)
                {
                    if(densityFunction is TypeInformation featureType)
                    {
                        var members = featureType.GetProperties();

                        var member = members.FirstOrDefault(x => x.Name == elementName.ToPascalCase());

                        if (member != null)
                        {
                            var property = (IPropertySymbol)member;

                            if (numbers.Contains(property.Type.Name))
                            {
                                AppendNumber(builder, elementName, element, property.Type.Name, newLine);
                                break;
                            }
                        }
                    }

                    builder.Indent().Append($"{elementName.ToPascalCase()} = new ConstantDensityFunction {{ Argument = ");
                    AppendUnknownNumber(builder, element, false);
                    builder.Append("}, ").Line();

                    break;
                }

                builder.Indent().Append($"{elementName.ToPascalCase()} = ");

                AppendUnknownNumber(builder, element, false);

                builder.Line();
                break;
            case JsonValueKind.Array:
                builder.Indent().Append($"{elementName.ToPascalCase()} = [");

                foreach(var arrayItem in element.EnumerateArray())
                    AppendArrayItem(cleanedNoises, arrayItem, builder);

                builder.Append("],").Line();
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetBoolean().ToString().ToLower()}, ", newLine);
                break;
            default:
                {
                    if (TryAppendTypeProperty(cleanedNoises, elementName, element, builder, newLine))
                        break;

                    if (TryAppendStateProperty(elementName, element, builder))
                        break;

                    builder.Type($"{elementName.ToPascalCase()} = new()");

                    foreach (var childProperty in element.EnumerateObject())
                    {
                        var childName = childProperty.Name;
                        var childValue = childProperty.Value;

                        AppendChildProperty(cleanedNoises, childName, childValue, builder, newLine, elementName == "noise_router");
                    }

                    builder.EndScope(", ", false);

                    break;
                }
        }
    }

    private static void AppendArrayItem(CleanedNoises cleanedNoises, JsonElement element, CodeBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Append($"\"{element.GetString()}\", ");
                break;
            case JsonValueKind.Number:
                AppendUnknownNumber(builder, element, false);
                break;
            case JsonValueKind.Array:
                //builder.Append($"[, ");

                //foreach (var arrayItem in element.EnumerateArray())
                //    AppendArrayItem(cleanedNoises, arrayItem, builder);
                
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Append($"{element.GetBoolean().ToString().ToLower()}, ");
                break;
            default:
                {
                    //builder.Type("new()");

                    //foreach (var childProperty in element.EnumerateObject())
                    //{
                    //    var childName = childProperty.Name;
                    //    var childValue = childProperty.Value;

                    //    AppendChildProperty(cleanedNoises, childName, childValue, builder);
                    //}

                    //builder.EndScope(", ", false);

                    break;
                }
        }
    }

    private static bool TryAppendTypeProperty(CleanedNoises cleanedNoises, string? elementName,
        JsonElement element, CodeBuilder builder, bool newLine = false)
    {
        var typeName = element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("type", out var typeElement) ? typeElement.GetString()! : string.Empty;

        if (element.ValueKind == JsonValueKind.String)
            typeName = element.GetString()!;

        if (cleanedNoises.StaticDensityFunctions.TryGetValue(typeName, out var staticDensityFunction))
        {
            var name = elementName != null ? $"{elementName.ToPascalCase()} = {staticDensityFunction}," :
                string.Empty;

            builder.Line(name);
        }
        else if (cleanedNoises.NoiseTypes.TryGetValue(typeName, out var noiseType))
        {
            var name = elementName != null ? $"{elementName.ToPascalCase()} = {noiseType}," :
               string.Empty;

            builder.Line(name);
        }
        else if (element.ValueKind == JsonValueKind.Object && cleanedNoises.WorldgenProperties.TryGetValue(typeName, out var featureType))
        {
            var name = elementName != null ? $"{elementName.ToPascalCase()} = new {featureType.Symbol.Name}()" :
                string.Empty;

            builder.Type(name);

            foreach (var childProperty in element.EnumerateObject().Where(x => x.Name != "type"))
            {
                var childName = childProperty.Name;
                var childValue = childProperty.Value;

                AppendChildProperty(cleanedNoises, childName, childValue, builder, true, true, featureType);
            }
            builder.EndScope(",", false);
        }
        else
            return false;

        if (newLine)
            builder.Line();

        return true;
    }

    private static bool TryAppendStateProperty(string elementName, JsonElement element, CodeBuilder builder)
    {
        var isState = elementName is defaultBlock or defaultFluid;

        if (isState)
        {
            builder.Indent().Append($"{elementName.ToPascalCase()} = new() {{ ");

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

            builder.Append("}, ").Line();
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

    private static void AppendUnknownNumber(CodeBuilder builder, JsonElement element, bool newLine = true)
    {
        if (element.TryGetInt16(out var shortValue))
            builder.AppendSimple($"{shortValue},", newLine);
        else if (element.TryGetInt32(out var intValue))
            builder.AppendSimple($"{intValue},", newLine);
        else if (element.TryGetInt64(out var longValue))
            builder.AppendSimple($"{longValue},", newLine);
        else if (element.TryGetDouble(out var doubleValue))
            builder.AppendSimple($"{doubleValue}d,", newLine);
        else if (element.TryGetSingle(out var floatValue))
            builder.AppendSimple($"{floatValue}f,", newLine);
    }
}
