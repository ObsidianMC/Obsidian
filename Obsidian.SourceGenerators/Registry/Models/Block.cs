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
    public BlockProperty[] Properties { get; }
    public Dictionary<int, List<string>> StateValues { get; }

    private Block(string name, string tag, int baseId, int defaultId, int registryId, int statesCount, BlockProperty[] properties,
        Dictionary<int, List<string>> stateValues)
    {
        Name = name;
        Tag = tag;
        BaseId = baseId;
        DefaultId = defaultId;
        RegistryId = registryId;
        StatesCount = statesCount;
        Properties = properties;
        StateValues = stateValues;
    }

    public static Block Get(JsonProperty property, int id)
    {
        string name = property.Name.RemoveNamespace().ToPascalCase();

        (int baseId, int defaultId, int statesCount, var stateValues) = GetIds(property);
        BlockProperty[] properties = GetBlockProperties(property).ToArray();

        return new Block(name, property.Name, baseId, defaultId, id, statesCount, properties, stateValues);
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
