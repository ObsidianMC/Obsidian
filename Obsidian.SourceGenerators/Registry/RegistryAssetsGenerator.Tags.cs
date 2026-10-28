using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;

namespace Obsidian.SourceGenerators.Registry;

public partial class RegistryAssetsGenerator
{
    private static void GenerateTags(Assets assets, SourceProductionContext context)
    {
        var builder = new CodeBuilder();
        builder.Line();
        builder.Namespace("Obsidian.API.Registries");
        builder.Line();
        builder.Type("public static partial class TagsRegistry");

        var syncedTags = assets.Tags.Where(tag => !tag.ServerOnly).ToArray();
        var tags = syncedTags.GroupBy(tag => tag.Type.GetActualType()).ToDictionary(x => x.Key, x => x.ToImmutableList());
        var skip = new List<string>();
        foreach (var childTags in tags)
        {
            builder.Type($"public static class {childTags.Key.ToPascalCase()}");
           
            // Each subfolder's tags (worldgen/biome) get a class of their own, even a single one (worldgen/feature), which
            // the All and Categories lists below refer to them through.
            var tagGroups = childTags.Value.GroupBy(tag => tag.Type.GetActualType(1));
            foreach (var groupedTags in tagGroups)
            {
                if (childTags.Key == groupedTags.Key)
                    continue;

                builder.Type($"public static class {groupedTags.Key.ToPascalCase()}");

                skip.Add(groupedTags.Key);

                foreach (var tag in groupedTags)
                {
                    builder.Line($"public static Tag {tag.PropertyName} {{ get; }} = new Tag {{ Name = {SymbolDisplay.FormatLiteral(tag.Identifier, true)}, Type = {SymbolDisplay.FormatLiteral(tag.Type, true)}, " +
                        $"Entries = [{string.Join(", ", tag.Values.Select(value => value.GetTagValue()))}] }};");
                }

                // After the tags: static initializers run in order, so an earlier All would hold nulls.
                builder.Line($"public static Tag[] All {{ get; }} = new[] {{ {string.Join(", ", groupedTags.Select(tag => tag.PropertyName))} }};");

                builder.EndScope();
            }

            foreach (var tag in childTags.Value)
            {
                if (skip.Contains(tag.Type.GetActualType(1)))
                    continue;

                builder.Line($"public static Tag {tag.PropertyName} {{ get; }} = new Tag {{ Name = {SymbolDisplay.FormatLiteral(tag.Identifier, true)}, " +
                    $"Type = {SymbolDisplay.FormatLiteral(tag.Type, true)}, Entries = [{string.Join(", ", tag.Values.Select(value => value.GetTagValue()))}] }};");
            }

            builder.Line($"public static Tag[] All {{ get; }} = new[] {{ {string.Join(", ", childTags.Value.Select(tag => tag.CompileName()))} }};");

            builder.EndScope();
        }

        builder.Line();
       
        builder.Method($"public static Dictionary<string, Tag[]> Categories = new()");
        foreach (var tagItem in tags)
        {
            builder.Indent().Append($"{{ {SymbolDisplay.FormatLiteral(tagItem.Key, true)}, new Tag[] {{ ");
            foreach (Tag tag in tagItem.Value)
            {
                if(tag.Type == tag.Parent)
                    builder.Append(tag.Type.ToPascalCase()).Append(".").Append(tag.PropertyName).Append(", ");
                else
                    builder.Append(tag.Parent.ToPascalCase())
                        .Append(".")
                        .Append(tag.Type.GetActualType(1).ToPascalCase())
                        .Append(".")
                        .Append(tag.PropertyName)
                        .Append(", ");
            }
            builder.Append("} }, ");
            builder.Line();
        }
        builder.Line().EndScope(true);

        // Temporary legacy worldgen tags are discoverable by BlockSet/BiomeSet through TryGet, but never enter
        // Categories or Worldgen.Biome.All, which GetCategories uses for the client sync.
        var serverTags = assets.Tags.Where(tag => tag.ServerOnly).Select(tag =>
            $"new Tag {{ Namespace = {SymbolDisplay.FormatLiteral(tag.Namespace, true)}, Name = {SymbolDisplay.FormatLiteral(tag.Identifier, true)}, " +
            $"Type = {SymbolDisplay.FormatLiteral(tag.Type, true)}, Entries = [{string.Join(", ", tag.Values.Select(value => value.GetTagValue()))}] }}");
        builder.Line($"public static Tag[] All {{ get; }} = new Tag[] {{ {string.Join(", ", syncedTags.Select(tag => tag.CompileName(true)).Concat(serverTags))} }};");

        builder.EndScope();

        context.AddSource("TagsRegistry.g.cs", builder.ToString());

        // One bridge for hard-coded worldgen tags. Remove with Datagen.LegacyTags in Phases 4/5; callers can then
        // select minecraft again here while the old worldgen helpers are replaced.
        context.AddSource("LegacyWorldgenTags.g.cs", """
            namespace Obsidian.API.Registries;

            public static class LegacyWorldgenTags
            {
                private const string Namespace = "obsidian_legacy";

                public static global::Obsidian.API.World.Features.BlockSet Blocks(string path) => new($"#{Namespace}:{path}");
                public static global::Obsidian.API.World.Features.BiomeSet Biomes(string path) => new($"#{Namespace}:{path}");
                public static Tag Block(string path) => TagsRegistry.TryGet("block", $"{Namespace}:{path}", out var tag)
                    ? tag : throw new global::System.InvalidOperationException($"Missing legacy worldgen block tag {path}.");
            }
            """);
    }

  
}
