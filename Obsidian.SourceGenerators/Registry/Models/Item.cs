using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class Item : IHasName, ITaggable
{
    public string Name { get; }
    public string Tag { get; }
    public int Id { get; }

    // Default component values, from item_components.json.
    public int MaxStackSize { get; }
    public int MaxDamage { get; }
    public int Enchantable { get; }

    public string Type => "item";
    public string Parent => "item";

    private Item(string name, string tag, int id, int maxStackSize, int maxDamage, int enchantable)
    {
        Name = name;
        Tag = tag;
        Id = id;
        MaxStackSize = maxStackSize;
        MaxDamage = maxDamage;
        Enchantable = enchantable;
    }

    /// <param name="components">The item's default components, or an undefined element when they're unknown.</param>
    public static Item Get(JsonProperty property, JsonElement components)
    {
        string tag = property.Name;
        string name = tag.RemoveNamespace().ToPascalCase();
        int id = property.Value.GetProperty("protocol_id").GetInt32();

        int maxStackSize = 64, maxDamage = 0, enchantable = 0;
        if (components.ValueKind == JsonValueKind.Object)
        {
            if (components.TryGetProperty("minecraft:max_stack_size", out var stackSize))
                maxStackSize = stackSize.GetInt32();

            if (components.TryGetProperty("minecraft:max_damage", out var damage))
                maxDamage = damage.GetInt32();

            if (components.TryGetProperty("minecraft:enchantable", out var enchantableValue))
                enchantable = enchantableValue.GetProperty("value").GetInt32();
        }

        return new Item(name, tag, id, maxStackSize, maxDamage, enchantable);
    }

    public string GetTagValue() => Id.ToString();
}
