using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Assets
{
    public Block[] Blocks { get; }
    public Tag[] Tags { get; }

    private Assets(Block[] blocks, Tag[] tags)
    {
        Blocks = blocks;
        Tags = tags;
    }

    public static Assets Get(GeneratorExecutionContext context)
    {
        Block[] blocks = GetBlocks(GetAsset(context, "blocks.json"));
        Tag[] tags = GetTags(GetAsset(context, "tags.json"), blocks);

        return new Assets(blocks, tags);
    }

    private static Block[] GetBlocks(string json)
    {
        var blocks = new List<Block>();
        using var document = JsonDocument.Parse(json);

        int id = 0;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            blocks.Add(Block.Get(property, id++));
        }

        return blocks.ToArray();
    }

    private static Tag[] GetTags(string json, Block[] blocks)
    {
        var taggables = new Dictionary<string, ITaggable>();
        foreach (Block block in blocks)
        {
            taggables.Add(block.Tag, block);
        }

        var tags = new List<Tag>();
        var knownTags = new Dictionary<string, Tag>();
        using var document = JsonDocument.Parse(json);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            tags.Add(Tag.Get(property, taggables, knownTags));
        }

        return tags.ToArray();
    }

    private static string GetAsset(GeneratorExecutionContext context, string fileName)
    {
        AdditionalText? asset = context.AdditionalFiles.FirstOrDefault(additionalText => additionalText.Path.EndsWith(fileName));
        if (asset is null)
        {
            throw new System.IO.FileNotFoundException("Asset not found.", fileName);
        }
        return asset.GetText()?.ToString() ?? string.Empty;
    }
}
