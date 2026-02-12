using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Packets;
using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenNoiseRegistryGenerator
{
    private static readonly string[] numbers = ["Int32", "Single", "Double", "Int64"];

    private static void AppendChildProperty(CleanedNoises cleanedNoises, string elementName,
        JsonElement element, CodeBuilder builder, bool isDensityFunction = false, TypeInformation? densityFunction = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (TryAppendTypeProperty(cleanedNoises, elementName, element, builder))
                    break;
                if (elementName == "type" && isDensityFunction)
                    break;

                builder.Line($"{elementName.ToPascalCase()} = {SymbolDisplay.FormatLiteral(element.GetString()!, true)}, ");
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
                //var hasObjects = element.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.Object;

                builder.Array($"{elementName.ToPascalCase()} =");

                //if (elementName == "spawn_target")
                //    hasObjects = false;

                foreach (var arrayItem in element.EnumerateArray())
                    AppendArrayItem(cleanedNoises, arrayItem, builder);

                builder.EndArrayScope(",", false);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Line($"{elementName.ToPascalCase()} = {element.GetBoolean().ToString().ToLower()}, ");
                break;
            default:
                {
                    if (builder.TryAppendStateProperty(elementName, element))
                        break;

                    if (TryAppendTypeProperty(cleanedNoises, elementName, element, builder))
                        break;

                    if (elementName == "spline" && TryAppendSplineProperty(cleanedNoises, elementName, element, builder))
                        break;

                    builder.Type($"{elementName.ToPascalCase()} = new()");

                    foreach (var childProperty in element.EnumerateObject())
                    {
                        var childName = childProperty.Name;
                        var childValue = childProperty.Value;

                        AppendChildProperty(cleanedNoises, childName, childValue, builder, elementName == "noise_router");
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
                builder.Line($"{SymbolDisplay.FormatLiteral(element.GetString()!, true)}, ");
                break;
            case JsonValueKind.Number:
                builder.Line($"{element},");
                break;
            case JsonValueKind.Array:
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Line($"{element.GetBoolean().ToString().ToLower()}, ");
                break;
            default:
                {
                    if (TryAppendTypeProperty(cleanedNoises, null, element, builder))
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

    private static bool TryAppendSplineProperty(CleanedNoises cleanedNoises, string? elementName, JsonElement element,
        CodeBuilder builder, bool appendName = true)
    {
        if (appendName)
            builder.Type($"{elementName!.ToPascalCase()} = new()");

        var passed = true;
        foreach (var childProperty in element.EnumerateObject())
        {
            var propName = childProperty.Name;
            var typeProperty = childProperty.Value;

            var typeName = typeProperty.ValueKind == JsonValueKind.String ? typeProperty.GetString() : string.Empty;

            if (TryGetCallableName(cleanedNoises, typeName, elementName, out var elementCallableName))
            {
                var name =  !string.IsNullOrEmpty(elementName) ? $"{propName.ToPascalCase()} = {elementCallableName}," :
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

                            TryAppendSplineProperty(cleanedNoises, childName, value, builder, false);

                            builder.EndScope(",", false);

                            continue;
                        }

                        if (value.ValueKind == JsonValueKind.Object && TryAppendSplineProperty(cleanedNoises, childName, value, builder))
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

        return passed;
    }

    private static bool TryAppendTypeProperty(CleanedNoises cleanedNoises, string? elementName,
        JsonElement element, CodeBuilder builder)
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

                AppendChildProperty(cleanedNoises, childName, childValue, builder, true, featureType);
            }
            builder.EndScope(",", false);
        }
        else
            return false;

        return true;
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
