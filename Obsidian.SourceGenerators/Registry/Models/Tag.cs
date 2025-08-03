using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Tag
{
    public string PropertyName { get; }
    public string Identifier { get; }
    public string Type { get; }
    public string Parent => this.Type.Contains('/') ? this.Type.Split('/')[0] : this.Type;
    public List<ITaggable> Values { get; }


    private Tag(string name, string type, List<ITaggable> values)
    {
        PropertyName = name.ToPascalCase();
        Identifier = name;
        Type = type;
        Values = values;
    }

    public static Tag Get(JsonProperty property, List<ITaggable> taggables, Dictionary<string, Tag> knownTags, Dictionary<string, List<string>> missedTags)
    {
        JsonElement propertyValues = property.Value;

        var type = propertyValues.GetProperty("type").GetString()!;
        var name = propertyValues.GetProperty("name").GetString()!;

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
            else if (taggables.FirstOrDefault(x => x.Tag == valueTag && x.Type == type) is ITaggable taggable)
            {
                values.Add(taggable);
            }
            else
            {
                UpdateMissedTags(property.Name, valueTag, missedTags);
            }
        }

        var tag = new Tag(name, type, values);
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
