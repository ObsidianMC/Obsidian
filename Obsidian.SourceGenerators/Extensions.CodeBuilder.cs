using Obsidian.SourceGenerators.Registry;
using Obsidian.SourceGenerators.Registry.Models;
using System.Globalization;
using System.Text.Json;

namespace Obsidian.SourceGenerators;
public partial class Extensions
{
    internal static void ParseMonsterLightValue(this CodeBuilder builder, JsonElement element, SourceProductionContext ctx)
    {
        builder.Append("new() { ");

        if (element.ValueKind == JsonValueKind.Number)
            builder.Append($"IntValue = {element.GetInt32()} ");
        else
        {
            builder.Append("Value = new() { ");
            foreach (var property in element.EnumerateObject())
            {
                var name = property.Name.ToPascalCase();

                if (name == "Value")
                {
                    foreach (var valueProperty in property.Value.EnumerateObject())
                        builder.Append($"{valueProperty.Name.ToPascalCase()} = {valueProperty.Value.GetInt32()}, ");

                    continue;
                }

                builder.Append($"{name} = ");
                builder.AppendValueType(property.Value, ctx);
            }
            builder.Append("} ");
        }

        builder.Append("}, ");
    }

    internal static void ParseArray(this CodeBuilder builder, JsonElement element, SourceProductionContext ctx, bool isDictionary = false)
    {
        builder.Append("[");

        foreach (var value in element.EnumerateArray())
        {
            if (value.ValueKind is JsonValueKind.Object)
            {
                builder.ParseProperty(value, ctx, isDictionary);
                continue;
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                builder.ParseArray(element, ctx, isDictionary);
                continue;
            }

            builder.AppendValueType(value, ctx, isDictionary);
        }

        builder.Append("], ");
    }

    internal static void ParseProperty(this CodeBuilder builder, JsonElement element, SourceProductionContext ctx, bool isDictionary = false)
    {
        builder.Append("new() { ");

        var isArray = element.ValueKind == JsonValueKind.Array;

        if (isArray)
        {
            foreach (var value in element.EnumerateArray())
            {
                if (value.ValueKind is JsonValueKind.Object)
                {
                    builder.ParseProperty(value, ctx, isDictionary);
                    continue;
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    builder.ParseArray(value, ctx, isDictionary);
                    continue;
                }
                builder.AppendValueType(value, ctx, isDictionary);
            }
        }
        else
        {
            foreach (var property in element.EnumerateObject())
            {
                var value = property.Value;

                if (!isArray)
                {
                    if (isDictionary)
                    {
                        builder.Append($"{{ \"{property.Name}\", ");
                    }
                    else
                    {
                        var name = property.Name.ToPascalCase();
                        builder.Append($"{name} = ");
                    }
                }

                if (value.ValueKind is JsonValueKind.Object)
                {
                    builder.ParseProperty(value, ctx, isDictionary);
                    continue;
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    builder.ParseArray(value, ctx, isDictionary);
                    continue;
                }

                builder.AppendValueType(value, ctx, isDictionary);
            }
        }

        builder.Append("}, ");
    }

    internal static void AppendValueType(this CodeBuilder builder, JsonElement element, SourceProductionContext ctx, bool isDictionary = false)
    {
        if (isDictionary)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    builder.Append($"\"{element.GetString()}\" }},");
                    break;
                case JsonValueKind.Number:
                    {
                        if (element.TryGetInt32(out var intValue))
                            builder.Append($"{intValue} }},");
                        else if (element.TryGetInt64(out var longValue))
                            builder.Append($"{longValue} }},");
                        else if (element.TryGetSingle(out var floatValue))
                            builder.Append($"{floatValue.ToString(CultureInfo.InvariantCulture)}f }},");
                        else if (element.TryGetDouble(out var doubleValue))
                            builder.Append($"{doubleValue.ToString(CultureInfo.InvariantCulture)}d }},");
                        break;
                    }
                case JsonValueKind.True:
                case JsonValueKind.False:
                    builder.Append($"{element.GetBoolean().ToString().ToLower()} }},");
                    break;
                case JsonValueKind.Null:
                    break;
                default:
                    ctx.ReportDiagnostic(DiagnosticSeverity.Error, $"Found an invalid property type: {element.ValueKind} in json.");
                    break;
            }

            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Append($"\"{element.GetString()}\", ");
                break;
            case JsonValueKind.Number:
                {
                    if (element.TryGetInt32(out var intValue))
                        builder.Append($"{intValue}, ");
                    else if (element.TryGetInt64(out var longValue))
                        builder.Append($"{longValue}, ");
                    else if (element.TryGetSingle(out var floatValue))
                        builder.Append($"{floatValue.ToString(CultureInfo.InvariantCulture)}f, ");
                    else if (element.TryGetDouble(out var doubleValue))
                        builder.Append($"{doubleValue.ToString(CultureInfo.InvariantCulture)}d, ");
                    break;
                }
            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Append($"{element.GetBoolean().ToString().ToLower()}, ");
                break;
            case JsonValueKind.Null:
                break;
            default:
                ctx.ReportDiagnostic(DiagnosticSeverity.Error, $"Found an invalid property type: {element.ValueKind} in json.");
                break;
        }
    }

    internal static void GenerateSimpleCodec(this CodeBuilder builder, Codec[] codecs, string registryName, string codecKey, string codecType, SourceProductionContext ctx)
    {
        builder.Type($"public static class {registryName}");

        builder.Indent().Append($"public const string CodecKey = \"{codecKey}\";").Line().Line();
        builder.Indent().Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(codecs.Length, 2))};").Line().Line();

        foreach (var codec in codecs)
        {
            var propertyName = codec.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static {codecType} {propertyName} {{ get; }} = new() {{ Id = {codec.RegistryId}, Name = \"{codec.Name}\", Element = new() {{ ");

            foreach (var property in codec.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                builder.Append($"{name} = ");

                if (value.ValueKind == JsonValueKind.Object)
                {
                    builder.ParseProperty(value, ctx);
                    continue;
                }

                builder.AppendValueType(value, ctx);
            }

            builder.Append("} };").Line();
        }

        builder.Line().Statement($"public static FrozenDictionary<string, {codecType}> All {{ get; }} = new Dictionary<string, {codecType}>");

        foreach (var name in codecs.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".ToFrozenDictionary()", true).Line();

        builder.EndScope();
    }

    internal static void GenerateSimpleCodec(this CodeBuilder builder, Codec[] codecs, string registryName, string codecKey, string codecType,
        Action<string, JsonElement> parseProperty, SourceProductionContext ctx)
    {
        builder.Type($"public static class {registryName}");

        builder.Indent().Append($"public const string CodecKey = \"{codecKey}\";").Line().Line();
        builder.Indent().Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(codecs.Length, 2))};").Line().Line();

        foreach (var codec in codecs)
        {
            var propertyName = codec.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static {codecType} {propertyName} {{ get; }} = new() {{ Id = {codec.RegistryId}, Name = \"{codec.Name}\", Element = new() {{ ");

            foreach (var property in codec.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                parseProperty(name, value);
            }

            builder.Append("} };").Line();
        }

        builder.Line().Statement($"public static FrozenDictionary<string, {codecType}> All {{ get; }} = new Dictionary<string, {codecType}>");

        foreach (var name in codecs.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".ToFrozenDictionary()", true).Line();

        builder.EndScope();
    }
}
