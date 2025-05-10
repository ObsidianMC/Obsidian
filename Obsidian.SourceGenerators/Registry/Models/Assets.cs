using Obsidian.SourceGenerators.Packets;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Assets
{
    public Block[] Blocks { get; }
    public Tag[] Tags { get; }
    public Item[] Items { get; }

    public Dictionary<string, Codec[]> Codecs { get; }
    public IDictionary<string, List<Sound>> Sounds { get; }

    private Assets(Block[] blocks, Tag[] tags, Item[] items, Dictionary<string, Codec[]> codecs, IDictionary<string, List<Sound>> sounds)
    {
        Blocks = blocks;
        Tags = tags;
        Items = items;
        Codecs = codecs;
        Sounds = sounds;
    }

    public static Assets Get(ImmutableArray<(string name, string json)> files, SourceProductionContext ctx)
    {
        Block[] blocks = GetBlocks(files.GetJsonFromArray("blocks"));
        Fluid[] fluids = GetFluids(files.GetJsonFromArray("fluids"));
        Tag[] tags = GetTags(files.GetJsonFromArray("tags"), blocks, fluids);
        Item[] items = GetItems(files.GetJsonFromArray("items"));
        IDictionary<string, List<Sound>> sounds = GetSounds(files.GetJsonFromArray("sounds"));
        Dictionary<string, Codec[]> codecs = GetCodecs(files);

        return new Assets(blocks, tags, items, codecs, sounds);
    }

    public static Dictionary<string, Codec[]> GetCodecs(ImmutableArray<(string name, string json)> files)
    {
        return new Dictionary<string, Codec[]>
        {
            { "dimensions", ParseCodec(files.GetJsonFromArray("dimensions")) },
            { "biomes", ParseCodec(files.GetJsonFromArray("biomes")) },
            { "chat_type", ParseCodec(files.GetJsonFromArray("chat_type")) },
            { "damage_type", ParseCodec(files.GetJsonFromArray("damage_type")) },
            { "trim_pattern", ParseCodec(files.GetJsonFromArray("trim_pattern")) },
            { "trim_material", ParseCodec(files.GetJsonFromArray("trim_material")) },
            { "wolf_variant", ParseCodec(files.GetJsonFromArray("wolf_variant")) },
            { "painting_variant", ParseCodec(files.GetJsonFromArray("painting_variant")) }
        };
    }

    private static Codec[] ParseCodec(string json)
    {
        if (json is null)
            return [];

        using var document = JsonDocument.Parse(json);

        var codecs = new List<Codec>();

        var codecElements = document.RootElement.GetProperty("value").EnumerateArray();

        foreach (var codec in codecElements)
        {
            var obj = new Codec(codec.GetProperty("name").GetString()!, codec.GetProperty("id").GetInt32());

            foreach (var property in codec.EnumerateObject())
            {
                if (property.Name is "name" or "id")
                    continue;

                foreach (var elementProperty in property.Value.EnumerateObject())
                {
                    obj.Properties.Add(elementProperty.Name.ToPascalCase(), elementProperty.Value.Clone());
                }
            }

            codecs.Add(obj);
        }

        return codecs.ToArray();
    }

    public static IDictionary<string, List<Sound>> GetSounds(string? json)
    {
        if (json is null)
            return new Dictionary<string, List<Sound>>();

        using var document = JsonDocument.Parse(json);

        var soundsElement = document.RootElement.EnumerateObject();

        var splitSounds = new Dictionary<string, List<Sound>>();

        foreach (JsonProperty property in soundsElement)
        {
            var name = property.Name.RemoveNamespace();
            var parentName = name.Split('.')[0];
            var sound = new Sound(name, property.Value.GetProperty(Vocabulary.ProtocolId).GetInt32());

            if (splitSounds.TryGetValue(parentName, out var list))
                list.Add(sound);
            else
                splitSounds.Add(parentName, [sound]);
        }

        return splitSounds;
    }

    public static Fluid[] GetFluids(string? json)
    {
        if (json is null)
            return [];

        var fluids = new List<Fluid>();
        using var document = JsonDocument.Parse(json);

        var fluidProperties = document.RootElement.EnumerateObject();

        foreach (JsonProperty property in fluidProperties)
        {
            var name = property.Name;

            fluids.Add(new Fluid(name, name.Substring(name.IndexOf(':') + 1), property.Value.GetInt32()));
        }

        return fluids.ToArray();
    }

    public static Block[] GetBlocks(string? json)
    {
        if (json is null)
            return [];

        var blocks = new List<Block>();
        using var document = JsonDocument.Parse(json);

        int id = 0;

        var blockProperties = document.RootElement.EnumerateObject();

        var newBlocks = new Dictionary<int, JsonProperty>();

        foreach (JsonProperty property in blockProperties)
        {
            foreach (var state in property.Value.GetProperty("states").EnumerateArray())
            {
                if (state.TryGetProperty("default", out var element))
                {
                    newBlocks.Add(state.GetProperty("id").GetInt32(), property);
                    break;
                }
            }
        }

        foreach (var property in newBlocks.OrderBy(x => x.Key).Select(x => x.Value))
            blocks.Add(Block.Get(property, id++));

        return blocks.ToArray();
    }

    private static Item[] GetItems(string? json)
    {
        if (json is null)
            return [];

        var items = new List<Item>();
        using var document = JsonDocument.Parse(json);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            items.Add(Item.Get(property));
        }

        return items.ToArray();
    }

    public static Tag[] GetTags(string? json, Block[] blocks, Fluid[] fluids)
    {
        if (json is null)
            return [];

        var taggables = new Dictionary<string, ITaggable>();
        foreach (Block block in blocks)
        {
            if (block.Tag is "minecraft:water" or "minecraft:lava")//Skip fluids
                continue;

            taggables.Add(block.Tag, block);
        }

        foreach (Fluid fluid in fluids)
            taggables.Add(fluid.Tag, fluid);

        var tags = new List<Tag>();
        var knownTags = new Dictionary<string, Tag>();
        var missedTags = new Dictionary<string, List<string>>();

        using var document = JsonDocument.Parse(json);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            tags.Add(Tag.Get(property, taggables, knownTags, missedTags));
        }

        VerifyTags(knownTags, missedTags, taggables);
        VerifyTags(knownTags, missedTags, taggables);//I can't think of a better solution :skull:

        return tags.ToArray();
    }

    private static void VerifyTags(Dictionary<string, Tag> knownTags, Dictionary<string, List<string>> missedTags, Dictionary<string, ITaggable> taggables)
    {
        foreach (var missedTag in missedTags)
        {
            var propertyName = missedTag.Key;
            var tagsMissed = missedTag.Value;

            var prop = knownTags[propertyName];
            foreach (var tagMissed in tagsMissed)
            {
                if (knownTags.TryGetValue(tagMissed, out var tag))
                {
                    foreach (var value in tag.Values)
                    {
                        if (prop.Values.Contains(value))
                            continue;

                        prop.Values.Add(value);
                    }
                }
                else if (taggables.TryGetValue(tagMissed, out var taggable) && !prop.Values.Contains(taggable))
                {
                    prop.Values.Add(taggable);
                }
            }
        }
    }
}
