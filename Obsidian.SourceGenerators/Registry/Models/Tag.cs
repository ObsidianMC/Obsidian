using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Tag
{
    public string Name { get; }
    public string MinecraftName { get; }
    public string Type { get; }
    public bool Replace { get; }
    public List<ITaggable> Values { get; }

    private Tag(string name, string minecraftName, string type, bool replace, List<ITaggable> values)
    {
        Name = name;
        MinecraftName = minecraftName;
        Type = type;
        Replace = replace;
        Values = values;
    }

    public static Tag Get(JsonProperty property, Dictionary<string, ITaggable> taggables, Dictionary<string, Tag> knownTags)
    {
        JsonElement propertyValues = property.Value;

        string minecraftName = propertyValues.GetProperty("name").GetString()!;
        string name = minecraftName.ToPascalCase();
        string type = property.Name.Substring(0, property.Name.IndexOf('/'));
        bool replace = propertyValues.GetProperty("replace").GetBoolean();

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
            }
            else if (taggables.TryGetValue(valueTag, out ITaggable taggable))
            {
                values.Add(taggable);
            }
        }

        var tag = new Tag(name, minecraftName, type, replace, values);
        knownTags[property.Name] = tag;
        return tag;
    }
}
