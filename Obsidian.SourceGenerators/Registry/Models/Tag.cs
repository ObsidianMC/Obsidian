using System.Text.Json;
using System.Threading.Tasks;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Tag
{
    public string Name { get; }
    public string MinecraftName { get; }
    public string Type { get; }
    public List<ITaggable> Values { get; }

    private Tag(string name, string minecraftName, string type, List<ITaggable> values)
    {
        Name = name;
        MinecraftName = minecraftName;
        Type = type;
        Values = values;
    }

    public static Tag Get(JsonProperty property, Dictionary<string, ITaggable> taggables, Dictionary<string, Tag> knownTags, Dictionary<string, List<string>> missedTags)
    {
        JsonElement propertyValues = property.Value;

        string minecraftName = propertyValues.GetProperty("name").GetString()!;
        string name = minecraftName.ToPascalCase();
        string type = property.Name.Substring(0, property.Name.IndexOf('/'));

        var values = new List<ITaggable>();

        foreach (JsonElement value in propertyValues.GetProperty("values").EnumerateArray())
        {
            string valueTag = value.GetString()!;

            if (valueTag.StartsWith("#"))
            {
                valueTag = type + '/' + valueTag.Substring(valueTag.IndexOf(':') + 1);
                if (knownTags.TryGetValue(valueTag, out Tag knownTag))
                {
                    foreach (ITaggable taggable in knownTag.Values)
                    {
                        values.Add(taggable);
                    }
                }
                else
                {
                    UpdateMissedTags(property.Name, valueTag, missedTags);
                }
            }
            else if (taggables.TryGetValue(valueTag, out ITaggable taggable))
            {
                values.Add(taggable);
            }
            else
            {
                UpdateMissedTags(property.Name, valueTag, missedTags);
            }
        }

        var tag = new Tag(name, minecraftName, type, values);
        knownTags[property.Name] = tag;
        return tag;
    }

    private static void UpdateMissedTags(string propertyName, string valueTag, Dictionary<string, List<string>> missedTags)
    {
        if (!missedTags.ContainsKey(propertyName))
            missedTags.Add(propertyName, new() { valueTag });
        else
            missedTags[propertyName].Add(valueTag);
    }
}
