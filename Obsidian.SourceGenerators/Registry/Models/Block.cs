using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Block : ITaggable, IHasName, IRegistryItem
{
    public string Name { get; }
    public string Type => "block";
    public string Parent => "block";
    public string Tag { get; }
    public int BaseId { get; }
    public int DefaultId { get; }
    public int RegistryId { get; }
    public int StatesCount { get; }
    public string? PlacementClass { get; }
    /// <summary>The Mojang class declaring each virtual hook's override, by hook (randomTick, tick, updateShape).</summary>
    public IReadOnlyDictionary<string, string> OverrideClasses { get; }
    public BlockProperty[] Properties { get; }
    public Dictionary<int, List<string>> StateValues { get; }
    /// <summary>The sapling's dumped TreeGrower by field (features by identifier, secondaryChance as a float literal), or null.</summary>
    public IReadOnlyDictionary<string, string>? TreeGrower { get; }
    /// <summary>The ignite and burn odds vanilla's FireBlock registers for this block, or null if it isn't flammable.</summary>
    public (int Ignite, int Burn)? FireOdds { get; }
    /// <summary>Whether vanilla's BlockStateBase.ignitedByLava is true for this block's default state.</summary>
    public bool IgnitedByLava { get; }
    /// <summary>Whether this block is a vanilla TntBlock, which fire primes when it replaces it.</summary>
    public bool IsTnt { get; }

    private Block(string name, string tag, int baseId, int defaultId, int registryId, int statesCount,
        string? placementClass, IReadOnlyDictionary<string, string> overrideClasses, BlockProperty[] properties, Dictionary<int, List<string>> stateValues,
        IReadOnlyDictionary<string, string>? treeGrower, (int Ignite, int Burn)? fireOdds, bool ignitedByLava, bool isTnt)
    {
        Name = name;
        Tag = tag;
        BaseId = baseId;
        DefaultId = defaultId;
        RegistryId = registryId;
        StatesCount = statesCount;
        PlacementClass = placementClass;
        OverrideClasses = overrideClasses;
        Properties = properties;
        StateValues = stateValues;
        TreeGrower = treeGrower;
        FireOdds = fireOdds;
        IgnitedByLava = ignitedByLava;
        IsTnt = isTnt;
    }

    public static Block Get(JsonProperty property, int id, string? placementClass, IReadOnlyDictionary<string, string> overrideClasses,
        IReadOnlyDictionary<string, string>? treeGrower, (int Ignite, int Burn)? fireOdds, bool ignitedByLava, bool isTnt)
    {
        string name = property.Name.RemoveNamespace().ToPascalCase();

        (int baseId, int defaultId, int statesCount, var stateValues) = GetIds(property);
        BlockProperty[] properties = GetBlockProperties(property).ToArray();

        return new Block(name, property.Name, baseId, defaultId, id, statesCount, placementClass, overrideClasses, properties, stateValues, treeGrower, fireOdds, ignitedByLava, isTnt);
    }

    private static (int baseId, int defaultId, int stateCount, Dictionary<int, List<string>> stateValues) GetIds(JsonProperty property)
    {
        int baseId = int.MaxValue;
        int defaultId = int.MinValue;
        int stateCount = 0;

        var props = new Dictionary<int, List<string>>();
        foreach (JsonElement state in property.Value.GetProperty("states").EnumerateArray())
        {
            int id = state.GetProperty("id").GetInt32();

            if (id < baseId)
                baseId = id;

            if (state.TryGetProperty("default", out JsonElement _default) && _default.GetBoolean())
                defaultId = id;

            if (state.TryGetProperty("properties", out var jsonElement))
            {
                var values = new List<string>();
                foreach (JsonProperty jsonProperty in jsonElement.EnumerateObject())
                {
                    if (jsonProperty.Value.GetString() is string propertyValue)
                    {
                        values.Add(propertyValue.ToPascalCase());
                    }
                }

                props.Add(id, values);
            }
            stateCount++;
        }

        return (baseId, defaultId, stateCount, props);
    }

    private static IEnumerable<BlockProperty> GetBlockProperties(JsonProperty block)
    {
        if (block.Value.TryGetProperty("properties", out JsonElement properties))
        {
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                yield return BlockProperty.Get(property);
            }
        }
    }

    public string GetTagValue() => RegistryId.ToString();
}
