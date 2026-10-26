using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Pathfinding;

/// <summary>
/// Generates vanilla's <c>PathType</c> enum, its default malus table (<c>path_type_malus.json</c>) and the own-state
/// path type of every block state (<c>path_type_states.json</c>), both dumped by the asset generator from vanilla.
/// The <c>Obsidian.API</c> assembly gets the public <c>PathNodeType</c>; <c>Obsidian</c> gets the server tables.
/// </summary>
[Generator]
public sealed class PathTypeMalusGenerator : IIncrementalGenerator
{
    private const string MalusFile = "path_type_malus.json";
    private const string StatesFile = "path_type_states.json";

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        var files = ctx.AdditionalTextsProvider
            .Where(static file => Path.GetFileName(file.Path) is MalusFile or StatesFile)
            .Select(static (file, ct) => (name: Path.GetFileName(file.Path), json: file.GetText(ct)!.ToString()))
            .Collect();

        ctx.RegisterSourceOutput(ctx.CompilationProvider.Combine(files),
            static (spc, source) => Generate(spc, source.Left, source.Right));
    }

    private static void Generate(SourceProductionContext context, Compilation compilation,
        ImmutableArray<(string name, string json)> files)
    {
        if (compilation.AssemblyName is not ("Obsidian" or "Obsidian.API"))
            return;

        var malus = files.FirstOrDefault(static file => file.name == MalusFile).json;
        var states = files.FirstOrDefault(static file => file.name == StatesFile).json;
        var apiOnly = compilation.AssemblyName == "Obsidian.API";
        if (malus is null || (states is null && !apiOnly))
        {
            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("OBSPF001", "Missing path type data",
                "path_type_malus.json or path_type_states.json was not generated; run the asset generator", "Obsidian",
                DiagnosticSeverity.Error, true), Location.None));
            return;
        }

        var entries = new List<(string Name, double Malus)>();
        using (var document = JsonDocument.Parse(malus))
        {
            foreach (var property in document.RootElement.EnumerateObject())
                entries.Add((property.Name, property.Value.GetDouble()));
        }

        if (apiOnly)
        {
            // The plugin-facing enum needs the names only; the state table is server-side.
            EmitApiEnum(context, entries);
            return;
        }

        var stateTypes = new List<byte>();
        using (var document = JsonDocument.Parse(states!))
        {
            foreach (var element in document.RootElement.EnumerateArray())
                stateTypes.Add((byte)element.GetInt32());
        }

        var builder = new StringBuilder();
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Obsidian.Entities.AI;");
        builder.AppendLine();
        builder.AppendLine("/// <summary>Vanilla's pathfinding node types, in declaration order.</summary>");
        builder.AppendLine("internal enum PathType");
        builder.AppendLine("{");
        foreach (var entry in entries)
            builder.AppendLine($"    {entry.Name},");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("/// <summary>Vanilla's default malus for each path type: a negative value rejects the node.</summary>");
        builder.AppendLine("internal static class PathTypeMalus");
        builder.AppendLine("{");
        builder.AppendLine("    public static float Get(PathType type) => type switch");
        builder.AppendLine("    {");
        foreach (var entry in entries)
            builder.AppendLine($"        PathType.{entry.Name} => {entry.Malus.ToString("0.0", CultureInfo.InvariantCulture)}f,");
        builder.AppendLine("        _ => throw new ArgumentOutOfRangeException(nameof(type)),");
        builder.AppendLine("    };");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("/// <summary>Maps the server's path types to the plugin-facing names, one explicit arm per vanilla type.</summary>");
        builder.AppendLine("internal static class PathNodeMapping");
        builder.AppendLine("{");
        builder.AppendLine("    public static Obsidian.API.AI.PathNodeType ToPublic(PathType type) => type switch");
        builder.AppendLine("    {");
        foreach (var entry in entries)
            builder.AppendLine($"        PathType.{entry.Name} => Obsidian.API.AI.PathNodeType.{entry.Name},");
        builder.AppendLine("        _ => throw new ArgumentOutOfRangeException(nameof(type)),");
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public static PathType ToInternal(Obsidian.API.AI.PathNodeType type) => type switch");
        builder.AppendLine("    {");
        foreach (var entry in entries)
            builder.AppendLine($"        Obsidian.API.AI.PathNodeType.{entry.Name} => PathType.{entry.Name},");
        builder.AppendLine("        _ => throw new ArgumentOutOfRangeException(nameof(type)),");
        builder.AppendLine("    };");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("/// <summary>Vanilla's own-state path type for every block state id (WalkNodeEvaluator.getPathTypeFromState).</summary>");
        builder.AppendLine("internal static class PathTypeStates");
        builder.AppendLine("{");
        builder.AppendLine($"    private const string Encoded = \"{Convert.ToBase64String(stateTypes.ToArray())}\";");
        builder.AppendLine();
        builder.AppendLine("    private static readonly byte[] types = Convert.FromBase64String(Encoded);");
        builder.AppendLine();
        builder.AppendLine("    public static int Count => types.Length;");
        builder.AppendLine();
        builder.AppendLine("    public static PathType Get(int stateId) => stateId >= 0 && stateId < types.Length");
        builder.AppendLine("        ? (PathType)types[stateId]");
        builder.AppendLine("        : throw new ArgumentOutOfRangeException(nameof(stateId));");
        builder.AppendLine("}");

        context.AddSource("PathType.g.cs", builder.ToString());
    }

    // The public, plugin-facing node types: the same names as the server's PathType, in the same order.
    private static void EmitApiEnum(SourceProductionContext context, List<(string Name, double Malus)> entries)
    {
        var api = new StringBuilder();
        api.AppendLine("namespace Obsidian.API.AI;");
        api.AppendLine();
        api.AppendLine("/// <summary>Vanilla's path node types. Generated from the asset generator's path_type_malus.json.</summary>");
        api.AppendLine("public enum PathNodeType");
        api.AppendLine("{");
        foreach (var entry in entries)
            api.AppendLine($"    {entry.Name},");
        api.AppendLine("}");
        context.AddSource("PathNodeType.g.cs", api.ToString());
    }
}
