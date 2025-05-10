using Obsidian.SourceGenerators.Packets;
using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;
using static Obsidian.SourceGenerators.Constants;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenNoiseRegistryGenerator
{
    private static readonly string[] numbers = ["Int32", "Single", "Double", "Int64"];

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
                    if (densityFunction is TypeInformation featureType)
                    {
                        var members = featureType.GetProperties();

                        var member = members.FirstOrDefault(x => x.Name == elementName.ToPascalCase());

                        if (member != null)
                        {
                            var property = (IPropertySymbol)member;

                            if (numbers.Contains(property.Type.Name))
                            {
                                builder.Line($"{elementName.ToPascalCase()} = {element}, ");
                                break;
                            }
                        }
                    }

                    builder.Line($"{elementName.ToPascalCase()} = new ConstantDensityFunction {{ Argument = {element} }}, ");
                    break;
                }

                builder.Line($"{elementName.ToPascalCase()} = {element},");
                break;
            case JsonValueKind.Array:
                var hasObjects = element.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.Object;

                builder.Array($"{elementName.ToPascalCase()} =");

                foreach (var arrayItem in element.EnumerateArray())
                    AppendArrayItem(cleanedNoises, arrayItem, builder, hasObjects);

                builder.EndArrayScope(",", false);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetBoolean().ToString().ToLower()}, ", newLine);
                break;
            default:
                {
                    if (TryAppendStateProperty(elementName, element, builder))
                        break;

                    if (TryAppendTypeProperty(cleanedNoises, elementName, element, builder, newLine))
                        break;

                    if (elementName == "spline" && TryAppendSplineProperty(cleanedNoises, elementName, element, builder, newLine))
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

    private static void AppendArrayItem(CleanedNoises cleanedNoises, JsonElement element, CodeBuilder builder, bool hasObjects = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Line($"\"{element.GetString()}\", ");
                break;
            case JsonValueKind.Number:
                builder.Line($"{element},");
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
                    if (TryAppendTypeProperty(cleanedNoises, null, element, builder, hasObjects))
                        break;

                    builder.Type("new()");

                    foreach (var childProperty in element.EnumerateObject())
                    {
                        var childName = childProperty.Name;
                        var childValue = childProperty.Value;

                        AppendChildProperty(cleanedNoises, childName, childValue, builder, hasObjects);
                    }

                    builder.EndScope(", ", false);

                    break;
                }
        }
    }

    private static bool TryAppendSplineProperty(CleanedNoises cleanedNoises, string elementName, JsonElement element,
        CodeBuilder builder, bool newLine = false, bool appendName = true)
    {
        if (appendName)
            builder.Type($"{elementName.ToPascalCase()} = new()");

        var passed = true;
        foreach (var childProperty in element.EnumerateObject())
        {
            var propName = childProperty.Name;
            var typeProperty = childProperty.Value;

            var typeName = typeProperty.ValueKind == JsonValueKind.String ? typeProperty.GetString() : string.Empty;

            if (TryGetCallableName(cleanedNoises, typeName, elementName, out var elementCallableName))
            {
                var name = elementName != null ? $"{propName.ToPascalCase()} = {elementCallableName}," :
                    string.Empty;

                builder.Line(name);
                passed = true;
            }
            else if (typeProperty.ValueKind == JsonValueKind.Array)//This is the points array
            {
                builder.Array($"{propName.ToPascalCase()} =");
                foreach (var item in typeProperty.EnumerateArray())
                {
                    builder.Type("new()");

                    foreach (var childElement in item.EnumerateObject())
                    {
                        var childName = childElement.Name;
                        var value = childElement.Value;

                        if (childName == "value")
                        {
                            if (value.ValueKind != JsonValueKind.Object)
                            {
                                builder.Line($"{childName.ToPascalCase()} = new {Vocabulary.ConstantSpline} {{ Value = {value} }},");
                                continue;
                            }

                            builder.Type($"{childName.ToPascalCase()} = new {Vocabulary.Spline}()");

                            TryAppendSplineProperty(cleanedNoises, childName, value, builder, newLine, false);

                            builder.EndScope(",", false);

                            continue;
                        }

                        if (value.ValueKind == JsonValueKind.Object && TryAppendSplineProperty(cleanedNoises, childName, value, builder, newLine))
                            continue;

                        builder.Line($"{childName.ToPascalCase()} = {value},");
                    }

                    builder.EndScope(",", false);
                }
                builder.EndArrayScope(",", false);
            }
            else
                passed = false;
        }

        if (appendName)
            builder.EndScope(",", false);

        if (newLine)
            builder.Line();

        return passed;
    }

    private static bool TryAppendTypeProperty(CleanedNoises cleanedNoises, string? elementName,
        JsonElement element, CodeBuilder builder, bool newLine = false)
    {
        var typeName = element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("type", out var typeElement) ? typeElement.GetString()! : string.Empty;

        if (element.ValueKind == JsonValueKind.String)
            typeName = element.GetString()!;

        if (TryGetCallableName(cleanedNoises, typeName, elementName, out var callableName))
        {
            var name = elementName != null ? $"{elementName.ToPascalCase()} = {callableName}," :
                string.Empty;

            builder.Line(name);
        }
        else if (element.ValueKind == JsonValueKind.Object && cleanedNoises.WorldgenProperties.TryGetValue(typeName, out var featureType))
        {
            var name = elementName != null ? $"{elementName.ToPascalCase()} = new {featureType.Symbol.Name}()" :
                $"new {featureType.Symbol.Name}()";

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
        var isState = elementName is defaultBlock or defaultFluid or blockResult;

        if (isState)
        {
            builder.Type($"{elementName.ToPascalCase()} = new() ");

            builder.Line($"Name = \"{element.GetProperty("Name")}\", ");

            if (element.TryGetProperty("Properties", out var props))
            {
                builder.Type("Properties = new Dictionary<string, string>()");

                foreach (var prop in props.EnumerateObject())
                {
                    var childName = prop.Name;
                    var childValue = prop.Value;

                    builder.Line($" {{ \"{childName}\", \"{childValue.GetString()}\" }}, ");
                }

                builder.EndScope(false);
            }

            builder.EndScope(",", false);
        }

        return isState;
    }


    private static readonly string[] surfacePropNames = [Vocabulary.ISurfaceRule, Vocabulary.ISurfaceCondition];
    private static bool IsSurfaceType(CleanedNoises cleanedNoises, string typeName)
    {
        if (cleanedNoises.WorldgenProperties.TryGetValue(typeName, out var typeInfo))
        {
            var symbolName = typeInfo.Symbol.Name;

            return surfacePropNames.Contains(symbolName) || symbolName.EndsWith(Vocabulary.SurfaceCondition) || symbolName.EndsWith(Vocabulary.SurfaceRule);
        }

        return false;
    }

    private static bool TryGetCallableName(CleanedNoises cleanedNoises, string typeName, string? elementName, out string callableName)
    {
        if (IsSurfaceType(cleanedNoises, typeName) && elementName != Vocabulary.Noise)
        {
            callableName = string.Empty;
            return false;
        }

        return cleanedNoises.StaticDensityFunctions.TryGetValue(typeName, out callableName) || cleanedNoises.NoiseTypes.TryGetValue(typeName, out callableName);
    }


}
