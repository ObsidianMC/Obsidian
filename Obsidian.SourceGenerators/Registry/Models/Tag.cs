using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Tag
{
    public string Name { get; }
    public string Parent { get; }
    public string MinecraftName { get; }
    public string Type { get; }
    public List<ITaggable> Values { get; }

    private Tag(string name, string minecraftName, string type, List<ITaggable> values, string parent)
    {
        Name = name;
        MinecraftName = minecraftName;
        Type = type;
        Values = values;
        Parent = parent;
    }

    public static Tag Get(JsonProperty property, Dictionary<string, ITaggable> taggables, Dictionary<string, Tag> knownTags, Dictionary<string, List<string>> missedTags)
    {
        JsonElement propertyValues = property.Value;
        var tagTypes = property.Name.Split('/');
        var parent = tagTypes[0];

        string minecraftName = propertyValues.GetProperty("name").GetString()!;
        string name = minecraftName.ToPascalCase();
        string type = tagTypes.Length > 2 ? tagTypes.ElementAtOrDefault(1) : parent;

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

        var tag = new Tag(name, minecraftName, type, values, parent);
        knownTags[property.Name] = tag;
        return tag;
    }

    private static void UpdateMissedTags(string propertyName, string valueTag, Dictionary<string, List<string>> missedTags)
    {
        if (!missedTags.ContainsKey(propertyName))
            missedTags.Add(propertyName, [valueTag]);
        else
            missedTags[propertyName].Add(valueTag);
    }
}
