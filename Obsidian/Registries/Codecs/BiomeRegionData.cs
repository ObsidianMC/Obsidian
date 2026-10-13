using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Mojang.Regions;
using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Reads a biome region from a plugin's <c>data/&lt;namespace&gt;/biome_region</c> folder, Obsidian's own format: the
/// <c>dimension</c> it's for, its <c>weight</c>, and either the biomes it <c>replace</c>s in the dimension's own
/// layout or a parameter list of its own, <c>biomes</c>, like a <c>multi_noise</c> biome source's.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "dimension": "minecraft:overworld",
///   "weight": 2,
///   "replace": { "minecraft:plains": "ruby:crystal_plains" }
/// }
/// </code>
/// Biomes are vanilla's or plugins' registered ones, named. Unknown fields are errors, so typos don't go unnoticed.
/// </remarks>
internal static class BiomeRegionData
{
    /// <summary>
    /// The highest weight a region may have, so that weights add up without overflowing.
    /// </summary>
    public const int MaxWeight = 1000;

    /// <param name="identifier">The region's identifier, its file's.</param>
    /// <exception cref="InvalidDataException">The region isn't valid, saying where and what's expected.</exception>
    public static BiomeRegion Read(string identifier, JsonElement root)
    {
        Object(root, "");

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("dimension" or "weight" or "replace" or "biomes"))
                throw Error(property.Name, "isn't a field of a biome region");
        }

        var dimension = Identifier(Required(root, "", "dimension"), "dimension");
        var weight = Int(Required(root, "", "weight"), "weight", 1, MaxWeight);
        var hasReplace = TryGet(root, "replace", out var replace);
        var hasBiomes = TryGet(root, "biomes", out var biomes);

        return (hasReplace, hasBiomes) switch
        {
            (true, false) => Replacing(identifier, dimension, weight, replace, "replace"),
            (false, true) => new ParameterListRegion(
                identifier,
                dimension,
                weight,
                LevelStemData.ParameterList(biomes, "biomes")),
            (true, true) => throw Error("", "has both replace and biomes: expected one of them"),
            (false, false) => throw Error("", "expected replace or biomes")
        };
    }

    // {"<biome or #tag>": "<biome>", ...}, not empty. A tag's biomes are found when the region generates, as tags
    // may name biomes of plugins and data packs read later.
    private static ReplacingRegion Replacing(
        string identifier,
        string dimension,
        int weight,
        JsonElement value,
        string path)
    {
        Object(value, path);

        var replacements = new Dictionary<string, BiomeCodec>();
        var tags = ImmutableArray.CreateBuilder<(string Tag, BiomeCodec Biome)>();

        foreach (var property in value.EnumerateObject())
        {
            var entryPath = Field(path, property.Name);
            var isTag = property.Name.StartsWith('#');

            if (!TryParseIdentifier(isTag ? property.Name[1..] : property.Name, out var replaced))
                throw Error(entryPath, $"{property.Name} isn't an identifier or a tag");

            var biome = Biome(property.Value, entryPath);

            if (isTag)
            {
                tags.Add((replaced, biome));
                continue;
            }

            if (!CodecRegistry.TryGetBiome(replaced, out _))
                throw Error(entryPath, $"{replaced} isn't a biome");

            replacements[replaced] = biome;
        }

        if (replacements.Count == 0 && tags.Count == 0)
            throw Error(path, "can't be empty");

        return new ReplacingRegion(
            identifier,
            dimension,
            weight,
            replacements.ToFrozenDictionary(),
            tags.ToImmutable());
    }

    private static BiomeCodec Biome(JsonElement value, string path)
    {
        var identifier = Identifier(value, path);

        return CodecRegistry.TryGetBiome(identifier, out var biome)
            ? biome!
            : throw Error(path, $"{identifier} isn't a biome");
    }
}
