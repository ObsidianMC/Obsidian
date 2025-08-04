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
        builder.Type("public static class TagsRegistry");

        var tags = assets.Tags.GroupBy(tag => tag.Type.GetActualType()).ToDictionary(x => x.Key, x => x.ToImmutableList());
        var skip = new List<string>();
        foreach (var childTags in tags)
        {
            builder.Type($"public static class {childTags.Key.ToPascalCase()}");
           
            //Workaround for flat_level_generator_preset will change this up another time
            foreach (var groupedTags in childTags.Value.GroupBy(tag => tag.Type.GetActualType(1)).Where(x => x.Count() > 1 || x.Key == "flat_level_generator_preset"))
            {
                if (childTags.Key == groupedTags.Key)
                    continue;

                builder.Type($"public static class {groupedTags.Key.ToPascalCase()}");
                builder.Line($"public static Tag[] All {{ get; }} = new[] {{ {string.Join(", ", groupedTags.Select(tag => tag.PropertyName))} }};");

                skip.Add(groupedTags.Key);

                foreach (var tag in groupedTags)
                {
                    builder.Line($"public static Tag {tag.PropertyName} {{ get; }} = new Tag {{ Name = {SymbolDisplay.FormatLiteral(tag.Identifier, true)}, Type = {SymbolDisplay.FormatLiteral(tag.Type, true)}, " +
                        $"Entries = new int[] {{ {string.Join(", ", tag.Values.Select(value => value.GetTagValue()))} }} }};");
                }

                builder.EndScope();
            }

            foreach (var tag in childTags.Value)
            {
                if (skip.Contains(tag.Type.GetActualType(1)))
                    continue;

                builder.Line($"public static Tag {tag.PropertyName} {{ get; }} = new Tag {{ Name = {SymbolDisplay.FormatLiteral(tag.Identifier, true)}, " +
                    $"Type = {SymbolDisplay.FormatLiteral(tag.Type, true)}, Entries = new int[] {{ {string.Join(", ", tag.Values.Select(value => value.GetTagValue()))} }} }};");
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
                    builder.Append(tag.Parent.ToPascalCase()).Append(".").Append(tag.Type.GetActualType(1).ToPascalCase()).Append(".").Append(tag.PropertyName).Append(", ");
            }
            builder.Append("} }, ");
            builder.Line();
        }
        builder.Line().EndScope(true);

        builder.Line($"public static Tag[] All {{ get; }} = new[] {{ {string.Join(", ", assets.Tags.Select(tag => tag.CompileName(true)))} }};");

        builder.EndScope();

        context.AddSource("TagsRegistry.g.cs", builder.ToString());
    }

  
}
