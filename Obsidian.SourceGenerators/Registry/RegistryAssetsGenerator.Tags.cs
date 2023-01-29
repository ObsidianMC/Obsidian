using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

public partial class RegistryAssetsGenerator
{
    private static void GenerateTags(Assets assets, SourceProductionContext context)
    {
        var builder = new CodeBuilder();
        builder.Using("Obsidian.Net.Packets.Play.Clientbound");
        builder.Line();
        builder.Namespace("Obsidian.Registries");
        builder.Line();
        builder.Type("internal static class TagsRegistry");

        IEnumerable<IGrouping<string, Tag>> grouped = assets.Tags.GroupBy(tag => tag.Type);
        foreach (IGrouping<string, Tag> tagGroup in grouped)
        {
            builder.Type($"public static class {tagGroup.Key.ToPascalCase()}");
            builder.Line($"public static Tag[] All = new[] {{ {string.Join(", ", tagGroup.Select(tag => tag.Name))} }};");
            foreach (Tag tag in tagGroup)
            {
                builder.Line($"public static Tag {tag.Name} {{ get; }} = new Tag {{ Name = \"{tag.MinecraftName}\", Type = \"{tag.Type}\", Entries = new int[] {{ {string.Join(", ", tag.Values.Select(value => value.GetTagValue()))} }} }};");
            }
            builder.EndScope();
        }

        builder.Line();
        builder.Line($"public static Tag[] All = new[] {{ {string.Join(", ", assets.Tags.Select(tag => tag.Type.ToPascalCase() + "." + tag.Name))} }};");
        builder.Method($"public static Dictionary<string, Tag[]> Categories = new()");
        foreach (IGrouping<string, Tag> tagGroup in grouped)
        {
            builder.Indent().Append($"{{ \"{tagGroup.Key}\", new Tag[] {{ ");
            foreach (Tag tag in tagGroup)
            {
                builder.Append(tag.Type.ToPascalCase()).Append(".").Append(tag.Name).Append(", ");
            }
            builder.Append("} }, ");
            builder.Line();
        }
        builder.Line().EndScope(true);

        builder.EndScope();

        context.AddSource("TagsRegistry.g.cs", builder.ToString());
    }
}
