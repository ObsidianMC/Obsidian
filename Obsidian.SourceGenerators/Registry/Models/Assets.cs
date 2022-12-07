using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Assets
{
    public Block[] Blocks { get; }
    public Tag[] Tags { get; }
    public Item[] Items { get; }

    private Assets(Block[] blocks, Tag[] tags, Item[] items)
    {
        Blocks = blocks;
        Tags = tags;
        Items = items;
    }

    public static Assets Get(GeneratorExecutionContext context)
    {
        Block[] blocks = GetBlocks(GetAsset(context, "blocks.json"));
        Tag[] tags = GetTags(GetAsset(context, "tags.json"), blocks);
        Item[] items = GetItems(GetAsset(context, "items.json"));

        return new Assets(blocks, tags, items);
    }

    private static Block[] GetBlocks(string? json)
    {
        if (json is null)
            return Array.Empty<Block>();

        var blocks = new List<Block>();
        using var document = JsonDocument.Parse(json);

        int id = 0;

        var blockProperties = document.RootElement.EnumerateObject();

        var newBlocks = new Dictionary<int, JsonProperty>();

        foreach (JsonProperty property in blockProperties)
        {
            foreach(var state in property.Value.GetProperty("states").EnumerateArray())
            {
                if(state.TryGetProperty("default", out var element))
                {
                    newBlocks.Add(state.GetProperty("id").GetInt32(), property);
                    break;
                }
            }
        }

        foreach(var property in newBlocks.OrderBy(x => x.Key).Select(x => x.Value))
            blocks.Add(Block.Get(property, id++));

        return blocks.ToArray();
    }

    private static Item[] GetItems(string? json)
    {
        if (json is null)
            return Array.Empty<Item>();

        var items = new List<Item>();
        using var document = JsonDocument.Parse(json);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            items.Add(Item.Get(property));
        }

        return items.ToArray();
    }

    private static Tag[] GetTags(string? json, Block[] blocks)
    {
        if (json is null)
            return Array.Empty<Tag>();

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

    private static string? GetAsset(GeneratorExecutionContext context, string fileName)
    {
        AdditionalText? asset = context.AdditionalFiles.FirstOrDefault(additionalText => additionalText.Path.EndsWith(fileName));
        return asset?.GetText()?.ToString();
    }
}
