using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry;
using Obsidian.SourceGenerators.Registry.Models;
using System.Globalization;
using System.Text.Json;

namespace Obsidian.SourceGenerators;

internal static class ClassBuilder
{
    private static readonly string[] numbers = ["Int32", "Single", "Double", "Int64"];

    public static void AppendChildProperty(Dictionary<string, TypeInformation> featureTypes, BaseFeatureDictionary baseFeatureTypes,
        TypeInformation featureType, string elementName, JsonElement element, CodeBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Line($"{elementName.ToPascalCase()} = {SymbolDisplay.FormatLiteral(element.GetString()!, true)}, ");
                break;
            case JsonValueKind.Number:
                if (featureType.Symbol != null)
                {
                    var members = featureType.GetProperties();

                    var member = members.FirstOrDefault(x => x.Name == elementName.ToPascalCase());

                    if (member != null)
                    {
                        var property = (IPropertySymbol)member;

                        if (numbers.Contains(property.Type.Name))
                        {
                            AppendNumberProperty(builder, elementName, element, property.Type.Name);
                            break;
                        }
                    }
                }
                builder.Line($"{elementName.ToPascalCase()} = new ConstantIntProvider {{ Type = {SymbolDisplay.FormatLiteral("minecraft:constant", true)}, Value = {GetNumberValue(element)} }},");
                break;
            case JsonValueKind.Array:
                builder.Line($"{elementName.ToPascalCase()} = [],");
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Line($"{elementName.ToPascalCase()} = {element.GetBoolean().ToString().ToLower()}, ");
                break;
            default:
                {
                    if (TryAppendTypeProperty(featureTypes, baseFeatureTypes, elementName, element, builder))
                        break;

                    if (TryAppendStateProperty(elementName, element, builder))
                        break;

                    builder.Type($"{elementName.ToPascalCase()} = new()");

                    foreach (var childProperty in element.EnumerateObject())
                    {
                        var childName = childProperty.Name;
                        var childValue = childProperty.Value;

                        AppendChildProperty(featureTypes, baseFeatureTypes, featureType, childName, childValue, builder);
                    }

                    builder.EndScope(",", false);

                    break;
                }
        }
    }

    private static bool TryAppendTypeProperty(Dictionary<string, TypeInformation> featureTypes, BaseFeatureDictionary baseFeatureTypes, string elementName,
        JsonElement element, CodeBuilder builder)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (element.TryGetProperty("type", out var typeElement))
        {
            var typeName = typeElement.GetString()!;

            var value = featureTypes.GetValue(typeName) ?? baseFeatureTypes.GetValue(typeName);
            if (value is not TypeInformation featureType)
                return false;

            builder.Type($"{elementName.ToPascalCase()} = new {featureType.Symbol.Name}()");

            if (!featureType.IsConfiguredFeature)
                builder.Line($"Type = {SymbolDisplay.FormatLiteral(featureType.ResourceLocation, true)},");

            foreach (var childProperty in element.EnumerateObject().Where(x => x.Name != "type"))
            {
                var childName = childProperty.Name;
                var childValue = childProperty.Value;

                AppendChildProperty(featureTypes, baseFeatureTypes, featureType, childName, childValue, builder);
            }

            builder.EndScope(",", false);

            return true;
        }

        return false;
    }

    public static bool TryAppendStateProperty(string elementName, JsonElement element, CodeBuilder builder, bool isDictionary = false)
    {
        var isState = elementName == "state" || elementName is Constants.DefaultBlock or Constants.DefaultFluid or Constants.BlockResult;

        if (isState || isDictionary)
        {
            builder.Type($"{elementName.ToPascalCase()} = new()");

            builder.Line($"Name = {SymbolDisplay.FormatLiteral(element.GetProperty("Name").ToString(), true)}, ");

            if (element.TryGetProperty("Properties", out var props))
            {
                builder.Type("Properties = new()");

                foreach (var prop in props.EnumerateObject())
                {
                    var childName = prop.Name;
                    var childValue = prop.Value;

                    builder.Line($" {{ {SymbolDisplay.FormatLiteral(childName, true)}, {SymbolDisplay.FormatLiteral(childValue.GetString()!, true)} }}, ");
                }

                builder.EndScope();
            }

            builder.EndScope(",", false);
        }

        return isState;
    }

    public static void AppendNumberProperty(CodeBuilder builder, string elementName, JsonElement element, string numberType = "Int32")
    {
        var pascalCaseElementName = elementName.ToPascalCase();

        builder.Line($"{pascalCaseElementName} = {GetNumberValue(element, numberType)}");
    }

    private static string GetNumberValue(JsonElement element, string numberType = "Int32")
    {
        if (numberType == "Single")
            return $"{element.GetSingle().ToString(CultureInfo.InvariantCulture)}f,";
        else if (numberType == "Double")
            return $"{element.GetDouble().ToString(CultureInfo.InvariantCulture)}d,";

        return $"{element},";
    }
}
