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

    private Assets(Block[] blocks, Tag[] tags, Item[] items, Dictionary<string, Codec[]> codecs,
        IDictionary<string, List<Sound>> sounds)
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
        Item[] items = GetItems(files.GetJsonFromArray("items"), files.GetJsonFromArray("item_components"));
        Dictionary<string, Codec[]> codecs = GetCodecs(files);
        BiomeEntry[] biomes = [.. codecs["biomes"].Select(biome => new BiomeEntry(biome.Name, biome.RegistryId))];
        EnchantmentEntry[] enchantments = GetEnchantments(files.GetJsonFromArray("enchantments"));
        Tag[] tags = GetTags(files.GetJsonFromArray("tags"), blocks, items, fluids, biomes, enchantments);

        IDictionary<string, List<Sound>> sounds = GetSounds(files.GetJsonFromArray("sounds"));

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
            { "cat_variant", ParseCodec(files.GetJsonFromArray("cat_variant")) },
            { "chicken_variant", ParseCodec(files.GetJsonFromArray("chicken_variant")) },
            { "cow_variant", ParseCodec(files.GetJsonFromArray("cow_variant")) },
            { "frog_variant", ParseCodec(files.GetJsonFromArray("frog_variant")) },
            { "pig_variant", ParseCodec(files.GetJsonFromArray("pig_variant")) },
            { "wolf_variant", ParseCodec(files.GetJsonFromArray("wolf_variant")) },
            { "painting_variant", ParseCodec(files.GetJsonFromArray("painting_variant")) },
            { "wolf_sound_variant", ParseCodec(files.GetJsonFromArray("wolf_sound_variant")) },
            { "dialogs", ParseCodec(files.GetJsonFromArray("dialogs")) },
            { "zombie_nautilus_variant", ParseCodec(files.GetJsonFromArray("zombie_nautilus_variant")) }
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

    public static EnchantmentEntry[] GetEnchantments(string? json)
    {
        if (json is null)
            return [];

        using var document = JsonDocument.Parse(json);

        // Vanilla's enchantment registry is sorted by id; the index is the network id (as in EnchantmentsRegistry).
        return [.. document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select((name, id) => new EnchantmentEntry(name, id))];
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

    private static Item[] GetItems(string? json, string? componentsJson)
    {
        if (json is null)
            return [];

        var items = new List<Item>();
        using var document = JsonDocument.Parse(json);
        using var components = componentsJson is null ? null : JsonDocument.Parse(componentsJson);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            var itemComponents = components is not null && components.RootElement.TryGetProperty(property.Name, out var entry)
                ? entry.GetProperty("components")
                : default;

            items.Add(Item.Get(property, itemComponents));
        }

        return items.ToArray();
    }

    public static Tag[] GetTags(string? json, Block[] blocks, Item[] items, Fluid[] fluids, BiomeEntry[] biomes,
        EnchantmentEntry[] enchantments)
    {
        if (json is null)
            return [];

        var taggables = new List<ITaggable>();

        taggables.AddRange(blocks);
        taggables.AddRange(items);
        taggables.AddRange(fluids);
        taggables.AddRange(biomes);
        taggables.AddRange(enchantments);

        using var document = JsonDocument.Parse(json);

        var definitions = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value);
        var resolved = new Dictionary<string, Tag>();

        return document.RootElement.EnumerateObject().Select(property => Tag.Get(property.Name, definitions, taggables, resolved)).ToArray();
    }
}
