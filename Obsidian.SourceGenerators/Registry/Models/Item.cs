using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Item : IHasName, ITaggable
{
    public string Name { get; }
    public string Tag { get; }
    public int Id { get; }

    public string Type => "item";
    public string Parent => "item";

    private Item(string name, string tag, int id)
    {
        Name = name;
        Tag = tag;
        Id = id;
    }

    public static Item Get(JsonProperty property)
    {
        string tag = property.Name;
        string name = tag.RemoveNamespace().ToPascalCase();
        int id = property.Value.GetProperty("protocol_id").GetInt32();
        return new Item(name, tag, id);
    }

    public string GetTagValue() => Id.ToString();
}
