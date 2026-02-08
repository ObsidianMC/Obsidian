using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry;
using Obsidian.SourceGenerators.Registry.Models;
using System.Globalization;
using System.Text.Json;

namespace Obsidian.SourceGenerators;

internal static class ClassBuilder
{
    private static readonly string[] numbers = ["Int32", "Single", "Double", "Int64"];

    public static void AppendChildProperty(BaseFeatureDictionary baseFeatureTypes,
        TypeInformation featureType, string elementName, JsonElement element, CodeBuilder builder, INamedTypeSymbol? containingType = null)
    {
        var propertyName = elementName.ToPascalCase();

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Line($"{propertyName} = {SymbolDisplay.FormatLiteral(element.GetString()!, true)}, ");
                break;
            case JsonValueKind.Number:
                if (featureType.Symbol != null)
                {
                    var members = featureType.GetProperties();

                    var member = members.FirstOrDefault(x => x.Name == propertyName);

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

                if (containingType != null)
                {
                    if (IsNumberType(containingType) && numbers.Contains(containingType.Name))
                    {
                        AppendNumberProperty(builder, elementName, element, containingType.Name);
                        break;
                    }

                    var members = containingType.GetMembers().Where(x => x.Kind == SymbolKind.Property);
                    var member = members.FirstOrDefault(x => x.Name == propertyName);
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

                builder.Line($"{propertyName} = new ConstantIntProvider {{ Type = {SymbolDisplay.FormatLiteral("minecraft:constant", true)}, Value = {GetNumberValue(element)} }},");
                break;
            case JsonValueKind.Array:
                //builder.Line($"{elementName.ToPascalCase()} = [],");
                builder.Array($"{propertyName} =");

                //I wanna get the base type of the array property symbol
                var arrayElementType = GetArrayElementType(containingType);

                foreach (var arrayItem in element.EnumerateArray())
                    AppendArrayItem(baseFeatureTypes, arrayItem, builder, arrayElementType);

                builder.EndArrayScope(",", false);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Line($"{propertyName} = {element.GetBoolean().ToString().ToLower()}, ");
                break;
            default:
                {
                    if (TryAppendTypeProperty(baseFeatureTypes, elementName, element, builder))
                        break;

                    if (TryAppendStateProperty(elementName, element, builder))
                        break;

                    builder.Type($"{propertyName} = new()");

                    foreach (var childProperty in element.EnumerateObject())
                    {
                        var childName = childProperty.Name;
                        var childValue = childProperty.Value;

                        AppendChildProperty(baseFeatureTypes, featureType, childName, childValue, builder);
                    }

                    builder.EndScope(",", false);

                    break;
                }
        }
    }

    private static bool IsNumberType(ITypeSymbol? type)
    {
        return type?.SpecialType is
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_Single or
            SpecialType.System_Double;
    }

    private static bool TryAppendTypeProperty(BaseFeatureDictionary baseFeatureTypes, string? elementName,
        JsonElement element, CodeBuilder builder)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (element.TryGetProperty("type", out var typeElement))
        {
            var typeName = typeElement.GetString()!;

            var value = baseFeatureTypes.FeatureTypes.GetValue(typeName) ?? baseFeatureTypes.GetValue(typeName);
            if (value is not TypeInformation featureType)
                return false;

            var properties = featureType.Symbol.GetMembers().Where(x => x.Kind == SymbolKind.Property);

            if (!string.IsNullOrEmpty(elementName))
                builder.Type($"{elementName!.ToPascalCase()} = new {featureType.Symbol.Name}()");
            else
                builder.Type($"new {featureType.Symbol.Name}()");

            if (!featureType.IsConfiguredFeature)
                builder.Line($"Type = {SymbolDisplay.FormatLiteral(featureType.ResourceLocation, true)},");

            foreach (var childProperty in element.EnumerateObject().Where(x => x.Name != "type"))
            {
                var childName = childProperty.Name;
                var childValue = childProperty.Value;
                var containingType = (properties.FirstOrDefault(x => x.Name == childName.ToPascalCase()) as IPropertySymbol)?.Type as INamedTypeSymbol;

                AppendChildProperty(baseFeatureTypes, featureType, childName, childValue, builder, containingType);
            }

            builder.EndScope(",", false);

            return true;
        }

        return false;
    }

    public static bool TryAppendStateProperty(string? elementName, JsonElement element, CodeBuilder builder, bool isDictionary = false)
    {
        var isState = elementName is Constants.DefaultBlock or Constants.DefaultFluid or Constants.BlockResult or "state" || element.TryGetProperty("Properties", out _);

        if (isState || isDictionary)
        {
            if (!string.IsNullOrEmpty(elementName))
                builder.Type($"{elementName!.ToPascalCase()} = new()");
            else
                builder.Type("new()");

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

    private static void AppendArrayItem(BaseFeatureDictionary baseFeatureTypes, JsonElement element, CodeBuilder builder, ITypeSymbol? arrayType = null)
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
                builder.Array(string.Empty);

                foreach (var arrayItem in element.EnumerateArray())
                    AppendArrayItem(baseFeatureTypes, arrayItem, builder);

                builder.EndArrayScope(",", false);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Line($"{element.GetBoolean().ToString().ToLower()}, ");
                break;
            default:
                {
                    if (TryAppendTypeProperty(baseFeatureTypes, null, element, builder))
                        break;
                    if (TryAppendStateProperty(null, element, builder))
                        break;

                    builder.Type("new()");

                    foreach (var childProperty in element.EnumerateObject())
                    {
                        var childName = childProperty.Name;
                        var childValue = childProperty.Value;

                        var namedSymbol = (arrayType?.GetMembers().FirstOrDefault(x => x.Name == childName.ToPascalCase()) as IPropertySymbol)?.Type as INamedTypeSymbol;

                        AppendChildProperty(baseFeatureTypes, default, childName, childValue, builder, namedSymbol as INamedTypeSymbol);
                    }

                    builder.EndScope(", ", false);

                    break;
                }
        }
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

    public static ITypeSymbol? GetArrayElementType(ITypeSymbol? symbol)
    {
        if(symbol == null)
            return null;

        if(symbol is IArrayTypeSymbol arrayType)
            return arrayType.ElementType;

        // Generic collections: List<T>, IEnumerable<T>, ICollection<T>, IReadOnlyList<T>, ImmutableArray<T>, etc.
        if (symbol is INamedTypeSymbol named)
        {
            if (named.TypeArguments.Length == 1)
                return named.TypeArguments[0];

            // If it's an interface like IEnumerable<T> via inheritance
            foreach (var iface in named.AllInterfaces)
            {
                if (iface.Name == "IEnumerable" && iface.TypeArguments.Length == 1)
                    return iface.TypeArguments[0];
            }
        }

        return null;
    }
}
