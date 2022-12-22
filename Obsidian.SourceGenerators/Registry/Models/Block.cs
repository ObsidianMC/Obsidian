using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Block : ITaggable, IHasName
{
    public string Name { get; }
    public string Tag { get; }
    public int BaseId { get; }
    public int DefaultId { get; }
    public int NumericId { get; }
    public int StatesCount { get; }
    public BlockProperty[] Properties { get; }
    public Dictionary<int, List<string>> StateValues { get; }

    private Block(string name, string tag, int baseId, int defaultId, int numericId, int statesCount, BlockProperty[] properties,
        Dictionary<int, List<string>> stateValues)
    {
        Name = name;
        Tag = tag;
        BaseId = baseId;
        DefaultId = defaultId;
        NumericId = numericId;
        StatesCount = statesCount;
        Properties = properties;
        StateValues = stateValues;
    }

    public static Block Get(JsonProperty property, int id)
    {
        string name = property.Name.RemoveNamespace().ToPascalCase();

        (int baseId, int defaultId, var stateValues) = GetIds(property);
        BlockProperty[] properties = GetBlockProperties(property).ToArray();

        int statesCount = 1;
        foreach (BlockProperty blockProperty in properties)
        {
            statesCount *= blockProperty.Values.Length;
        }

        return new Block(name, property.Name, baseId, defaultId, id, statesCount, properties, stateValues);
    }

    private static (int baseId, int defaultId, Dictionary<int, List<string>> stateValues) GetIds(JsonProperty property)
    {
        int baseId = int.MaxValue;
        int defaultId = int.MinValue;

        var props = new Dictionary<int, List<string>>();
        foreach (JsonElement state in property.Value.GetProperty("states").EnumerateArray())
        {
            int id = state.GetProperty("id").GetInt32();

            if (id < baseId)
                baseId = id;

            if (state.TryGetProperty("default", out JsonElement _default) && _default.GetBoolean())
                defaultId = id;

            if(state.TryGetProperty("properties", out var jsonElement))
            {
                foreach (var properties in jsonElement.EnumerateObject())
                {
                    if (props.ContainsKey(id))
                        props[id].Add(properties.Value.GetString());
                    else
                        props.Add(id, new List<string>() { properties.Value.GetString() });
                }
            }
        }

        return (baseId, defaultId, props);
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

    public string GetTagValue() => NumericId.ToString();
}
