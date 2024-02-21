﻿using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenRegistryGenerator
{
    private static readonly string[] numbers = ["Int32", "Single", "Double", "Int64"];

   
    private static void BuildTreeType(Dictionary<string, TypeInformation> featureTypes, BaseFeatureDictionary baseFeatureTypes,
        Features features, CodeBuilder builder)
    {
        var treeFeatures = features.TreeFeatures;

        foreach (var treeFeature in treeFeatures)
        {
            var sanitizedName = treeFeature.Name.ToPascalCase();
            builder.Type($"public static readonly TreeFeature {sanitizedName} = new()");

            builder.Line($"Identifier = \"{treeFeature.Name}\", ");

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
                            if (TryAppendTypeProperty(featureTypes, baseFeatureTypes, elementName, element, builder, true))
                                break;
                            if (TryAppendStateProperty(elementName, element, builder))
                                break;

                            builder.Append($"{elementName.ToPascalCase()} = new() {{ ");

                            foreach (var childProperty in element.EnumerateObject())
                            {
                                var childName = childProperty.Name;
                                var childValue = childProperty.Value;

                                AppendChildProperty(featureTypes, baseFeatureTypes, default, childName, childValue, builder);
                            }

                            builder.Append("},");

                            break;
                        }
                }
            }

            builder.EndScope(true);
        }

        builder.Type("public static readonly FrozenDictionary<string, TreeFeature> All = new Dictionary<string, TreeFeature>()");

        foreach (var treeFeature in treeFeatures)
        {
            var sanitizedName = treeFeature.Name.ToPascalCase();

            builder.Line($"{{ \"{treeFeature.Name}\", {sanitizedName}}}, ");
        }

        builder.EndScope(".ToFrozenDictionary()", true);
    }

    private static void AppendChildProperty(Dictionary<string, TypeInformation> featureTypes, BaseFeatureDictionary baseFeatureTypes,
        TypeInformation featureType, string elementName, JsonElement element, CodeBuilder builder)
    {
        //Temp workaround :weary:
        if (elementName == "can_grow_through" && element.ValueKind != JsonValueKind.Array)
        {
            builder.Append($"{elementName.ToPascalCase()} = {{ \"{element.GetString()}\"}}, ");
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Append($"{elementName.ToPascalCase()} = \"{element.GetString()}\", ");
                break;
            case JsonValueKind.Number:
                var members = featureType.Symbol.GetMembers().Where(x => x.Kind == SymbolKind.Property).ToList();

                if (featureType.Symbol.BaseType != null)
                    members.AddRange(featureType.Symbol.BaseType.GetMembers().Where(x => x.Kind == SymbolKind.Property));

                foreach (var mem in members.ToList())
                {
                    var memProp = (IPropertySymbol)mem;
                    if (memProp.Type.Kind == SymbolKind.NamedType)
                        members.AddRange(memProp.Type.GetMembers().Where(x => x.Kind == SymbolKind.Property));
                }

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
                builder.Append($"{elementName.ToPascalCase()} = {{}}, ");
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Append($"{elementName.ToPascalCase()} = {element.GetBoolean().ToString().ToLower()}, ");
                break;
            default:
                {
                    if (TryAppendTypeProperty(featureTypes, baseFeatureTypes, elementName, element, builder))
                        break;

                    if (TryAppendStateProperty(elementName, element, builder))
                        break;

                    builder.Append($"{elementName.ToPascalCase()} = new() {{ ");

                    foreach (var childProperty in element.EnumerateObject())
                    {
                        var childName = childProperty.Name;
                        var childValue = childProperty.Value;

                        AppendChildProperty(featureTypes, baseFeatureTypes, featureType, childName, childValue, builder);
                    }

                    builder.Append("}, ");

                    break;
                }
        }
    }

    private static bool TryAppendTypeProperty(Dictionary<string, TypeInformation> featureTypes, BaseFeatureDictionary baseFeatureTypes, string elementName,
        JsonElement element, CodeBuilder builder, bool newLine = false)
    {
        if (element.TryGetProperty("type", out var typeElement))
        {
            var typeName = typeElement.GetString()!;

            var value = featureTypes.GetValue(typeName) ?? baseFeatureTypes.GetValue(typeName);
            if (value is not TypeInformation featureType)
                return false;

            builder.Indent().Append($"{elementName.ToPascalCase()} = new {featureType.Symbol.Name}() {{ Type = \"{featureType.ResourceLocation}\", ");

            foreach (var childProperty in element.EnumerateObject().Where(x => x.Name != "type"))
            {
                var childName = childProperty.Name;
                var childValue = childProperty.Value;

                AppendChildProperty(featureTypes, baseFeatureTypes, featureType, childName, childValue, builder);
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
        var isState = elementName == "state";

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

}
