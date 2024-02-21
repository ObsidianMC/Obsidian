using Microsoft.CodeAnalysis;
using Obsidian.SourceGenerators.Registry.Models;
using System.Linq;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenRegistryGenerator
{
    private static void AppendNumber(CodeBuilder builder, string elementName, JsonElement element, string numberType = "Int32", bool newLine = true)
    {
        if (numberType == "Int16")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetInt16()},", newLine);
        else if (numberType == "Int32")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetInt32()},", newLine);
        else if (numberType == "Int64")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetInt64()},", newLine);
        else if (numberType == "Single")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetSingle()},", newLine);
        else if (numberType == "Double")
            builder.AppendSimple($"{elementName.ToPascalCase()} = {element.GetDouble()},", newLine);
    }

    private static void AppendNumber(CodeBuilder builder, JsonElement element, string numberType = "Int32", bool newLine = true)
    {
        if (numberType == "Int16")
            builder.AppendSimple($"{element.GetInt16()},", newLine);
        else if (numberType == "Int32")
            builder.AppendSimple($"{element.GetInt32()},", newLine);
        else if (numberType == "Int64")
            builder.AppendSimple($" {element.GetInt64()},", newLine);
        else if (numberType == "Single")
            builder.AppendSimple($"{element.GetSingle()},", newLine);
        else if (numberType == "Double")
            builder.AppendSimple($" {element.GetDouble()},", newLine);
    }

    private static void BuildTreeType(Dictionary<string, TypeInformation> featureTypes, BaseFeatureDictionary baseFeatureTypes,
        Features features, CodeBuilder builder)
    {
        var treeFeatures = features.TreeFeatures;

        foreach (var treeFeature in treeFeatures)
        {
            var sanitizedName = treeFeature.Name.ToPascalCase();
            builder.Type($"public static readonly TreeFeature {sanitizedName} = new()");

            builder.Line($"Identifier = \"{treeFeature.Name}\",");

            foreach (var property in treeFeature.Properties)
            {
                var elementName = property.Name;
                var element = property.Value;

                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        builder.Line($"{elementName.ToPascalCase()} = \"{element.GetString()}\",");
                        break;
                    case JsonValueKind.Number:
                        AppendNumber(builder, elementName, element);
                        break;
                    case JsonValueKind.Array:
                        builder.Line($"{elementName.ToPascalCase()} = {{}},");
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        builder.Line($"{elementName.ToPascalCase()} = {element.GetBoolean().ToString().ToLower()},");
                        break;
                    default:
                        {
                            if (element.TryGetProperty("type", out var typeElement))
                            {
                                var typeName = typeElement.GetString()!;

                                var value = featureTypes.GetValue(typeName) ?? baseFeatureTypes.GetValue(typeName);
                                if (value is not TypeInformation featureType)
                                    break;

                                builder.Indent().Append($"{elementName.ToPascalCase()} = new {featureType.Symbol.Name}() {{ Type = \"{featureType.ResourceLocation}\", ");

                                foreach (var childProperty in element.EnumerateObject().Where(x => x.Name != "type"))
                                {
                                    var childName = childProperty.Name;
                                    var childValue = childProperty.Value;

                                    AppendChildProperty(featureTypes, baseFeatureTypes, featureType, childName, childValue, builder);
                                }

                                builder.Append(" },").Line();
                                break;
                            }

                            builder.Line($"{elementName.ToPascalCase()} = new(),");

                            break;
                        }
                }
            }

            builder.EndScope(true);
        }
    }

    private static readonly string[] numbers = ["Int32", "Single", "Double", "Int64"];

    private static void AppendChildProperty(Dictionary<string, TypeInformation> featureTypes, BaseFeatureDictionary baseFeatureTypes,
        TypeInformation featureType, string elementName, JsonElement element, CodeBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Append($"{elementName.ToPascalCase()} = \"{element.GetString()}\",");
                break;
            case JsonValueKind.Number:
                var members = featureType.Symbol.GetMembers().Where(x => x.Kind == SymbolKind.Property).ToList();

                if (featureType.Symbol.BaseType != null)
                    members.AddRange(featureType.Symbol.BaseType.GetMembers());

                var member = members.FirstOrDefault(x => x.Name == elementName.ToPascalCase());

                if (member != null)
                {
                    var property = (IPropertySymbol)member;

                    if (numbers.Contains(property.Type.Name))
                    {
                        AppendNumber(builder, elementName, element, property.Type.Name, false);
                        break;
                    }
                }

                builder.Append($"{elementName.ToPascalCase()} = new ConstantIntProvider {{ Type = \"minecraft:constant\", Value = ");

                AppendNumber(builder, element, newLine: false);
                builder.Append("}, ");
                break;
            case JsonValueKind.Array:
                builder.Append($"{elementName.ToPascalCase()} = {{}},");
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Append($"{elementName.ToPascalCase()} = {element.GetBoolean().ToString().ToLower()},");
                break;
            default:
                {
                    if (element.TryGetProperty("type", out var typeElement))
                    {
                        var typeName = typeElement.GetString()!;
                        var value = featureTypes.GetValue(typeName) ?? baseFeatureTypes.GetValue(typeName);
                        if (value is not TypeInformation otherFeatureType)
                            break;

                        builder.Append($"{elementName.ToPascalCase()} = new {otherFeatureType.Symbol.Name}() {{ Type = \"{otherFeatureType.ResourceLocation}\", ");

                        foreach (var childProperty in element.EnumerateObject().Where(x => x.Name != "type"))
                        {
                            var childName = childProperty.Name;
                            var childValue = childProperty.Value;

                            AppendChildProperty(featureTypes, baseFeatureTypes, otherFeatureType, childName, childValue, builder);
                        }

                        builder.Append(" },");


                        break;
                    }

                    builder.Append($"{elementName.ToPascalCase()} = new(),");

                    break;
                }
        }
    }
}
