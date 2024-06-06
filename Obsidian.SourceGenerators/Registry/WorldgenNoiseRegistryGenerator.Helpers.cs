using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

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
                                builder.AppendNumber(elementName, element, property.Type.Name, newLine);
                                break;
                            }
                        }
                    }

                    builder.Indent().Append($"{elementName.ToPascalCase()} = new ConstantDensityFunction {{ Argument = ");
                    builder.AppendUnknownNumber(element, false);
                    builder.Append("}, ").Line();

                    break;
                }

                builder.Indent().Append($"{elementName.ToPascalCase()} = ");

                builder.AppendUnknownNumber(element, false);

                builder.Line();
                break;
            case JsonValueKind.Array:
                var hasObjects = element.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.Object;

                if (hasObjects)
                    builder.Indent().Append($"{elementName.ToPascalCase()} =").Line().Indent().Append("[");
                else
                    builder.Indent().Append($"{elementName.ToPascalCase()} = [");

                foreach (var arrayItem in element.EnumerateArray())
                    AppendArrayItem(cleanedNoises, arrayItem, builder, hasObjects);

                if (hasObjects)
                    builder.Line("],");
                else
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

    private static void AppendArrayItem(CleanedNoises cleanedNoises, JsonElement element, CodeBuilder builder, bool hasObjects = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Append($"\"{element.GetString()}\", ");
                break;
            case JsonValueKind.Number:
                builder.AppendUnknownNumber(element, false);
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

                    builder.Line().Type("new()");

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

    private static bool TryAppendTypeProperty(CleanedNoises cleanedNoises, string? elementName,
        JsonElement element, CodeBuilder builder, bool newLine = false)
    {
        var typeName = element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("type", out var typeElement) ? typeElement.GetString()! : string.Empty;

        if (element.ValueKind == JsonValueKind.String)
            typeName = element.GetString()!;

        if (TryGetCallableName(cleanedNoises, typeName, out var callableName))
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

    private static bool TryGetCallableName(CleanedNoises cleanedNoises, string typeName, out string callableName) =>
        cleanedNoises.StaticDensityFunctions.TryGetValue(typeName, out callableName) ||
        cleanedNoises.NoiseTypes.TryGetValue(typeName, out callableName);
}
