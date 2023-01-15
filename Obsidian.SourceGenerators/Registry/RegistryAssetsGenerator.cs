using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class RegistryAssetsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var jsonFiles = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".json"))
            .Select(static (file, ct) => (name: Path.GetFileNameWithoutExtension(file.Path), content: file.GetText(ct)!.ToString()));

        var compilation = context.CompilationProvider.Combine(jsonFiles.Collect());

        context.RegisterSourceOutput(compilation, this.Generate);
    }

    private void Generate(SourceProductionContext context, (Compilation compilation, ImmutableArray<(string name, string json)> files) output)
    {
        var asm = output.compilation.AssemblyName;

        var assets = Assets.Get(output.files, context);

        if (asm == "Obsidian")
        {
            GenerateTags(assets, context);
            GenerateItems(assets, context);
            GenerateBlockIds(assets, context);
            GenerateCodecs(assets, context);
        }
        else if (asm == "Obsidian.API")
        {
            GenerateMaterials(assets, context);
        }
    }

    private static void ParseProperty(CodeBuilder builder, JsonElement element, SourceProductionContext ctx)
    {
        builder.Append("new() { ");

        var isArray = element.ValueKind == JsonValueKind.Array;

        if (isArray)
        {
            foreach (var value in element.EnumerateArray())
            {
                if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ParseProperty(builder, value, ctx);
                    continue;
                }

                AppendValueType(builder, value, ctx);
            }
        }
        else
        {
            foreach (var property in element.EnumerateObject())
            {
                var value = property.Value;

                if (!isArray)
                {
                    var name = property.Name.ToPascalCase();
                    builder.Append($"{name} = ");
                }

                if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ParseProperty(builder, value, ctx);
                    continue;
                }

                AppendValueType(builder, value, ctx);
            }
        }

        builder.Append("}, ");
    }

    private static void AppendValueType(CodeBuilder builder, JsonElement element, SourceProductionContext ctx)
    {
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
                    builder.Append($"{floatValue}f, ");
                else if (element.TryGetDouble(out var doubleValue))
                    builder.Append($"{doubleValue}d, ");
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
}
