using Obsidian.API.Registries;

namespace Obsidian.Registries;

/// <summary>
/// Resolves block tag references such as <c>#minecraft:mineable/pickaxe</c>.
/// </summary>
internal static class BlockTags
{
    /// <summary>
    /// The block tag with this id (with or without the <c>#</c>), or null if there's no such tag.
    /// </summary>
    public static Tag? Find(string tagId)
    {
        var id = tagId.TrimStart('#');
        var colon = id.IndexOf(':');
        var path = colon < 0 ? id : id[(colon + 1)..];
        return TagsRegistry.Block.All.FirstOrDefault(tag => FullPath(tag) == path);
    }

    // Most block tags are named by their path; the mineable ones keep the directory in Type (e.g. "block/mineable" + "axe").
    private static string FullPath(Tag tag) => tag.Type == "block" ? tag.Name : tag.Type["block/".Length..] + "/" + tag.Name;
}
