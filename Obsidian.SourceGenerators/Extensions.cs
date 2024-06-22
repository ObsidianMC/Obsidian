using Obsidian.SourceGenerators.Registry;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;

namespace Obsidian.SourceGenerators;
public static partial class Extensions
{
    public static string GetJsonFromArray(this ImmutableArray<(string name, string json)> array, string name) =>
        array.FirstOrDefault(x => x.name == name).json;

    internal static string CompileName(this Tag tag, bool last = false)
    {
        if(last)
            return tag.Parent == tag.Type ? $"{tag.Parent.ToPascalCase()}.{tag.Name}" : $"{tag.Parent.ToPascalCase()}.{tag.Type.ToPascalCase()}.{tag.Name}";

        return tag.Parent == tag.Type ? tag.Name : $"{tag.Type.ToPascalCase()}.{tag.Name}";
    }
}
