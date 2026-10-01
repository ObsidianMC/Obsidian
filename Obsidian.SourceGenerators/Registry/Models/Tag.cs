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

    /// <summary>
    /// Resolves the tag named <paramref name="propertyName"/>, expanding referenced tags in place like vanilla's
    /// tag loader: entries keep their declaration order and duplicates are dropped.
    /// </summary>
    /// <param name="definitions">Every raw tag definition, keyed by property name (e.g. <c>block/logs</c>).</param>
    /// <param name="resolved">Tags resolved so far; referenced tags are added to it as they're resolved.</param>
    public static Tag Get(string propertyName, IReadOnlyDictionary<string, JsonElement> definitions, List<ITaggable> taggables,
        Dictionary<string, Tag> resolved)
    {
        if (resolved.TryGetValue(propertyName, out var known))
            return known;

        var definition = definitions[propertyName];
        var type = definition.GetProperty("type").GetString()!;
        var name = definition.GetProperty("name").GetString()!;

        var tag = new Tag(name, type, []);
        resolved[propertyName] = tag;

        foreach (var value in definition.GetProperty("values").EnumerateArray())
        {
            var valueTag = value.GetString()!;

            if (valueTag.StartsWith("#"))
            {
                var reference = type + '/' + valueTag.Substring(valueTag.IndexOf(':') + 1);
                if (definitions.ContainsKey(reference))
                {
                    foreach (var taggable in Get(reference, definitions, taggables, resolved).Values)
                        tag.Add(taggable);
                }
            }
            else if (taggables.FirstOrDefault(x => x.Tag == valueTag && x.Type == type) is ITaggable taggable)
            {
                tag.Add(taggable);
            }
        }

        return tag;
    }

    private void Add(ITaggable taggable)
    {
        if (!this.Values.Contains(taggable))
            this.Values.Add(taggable);
    }
}
