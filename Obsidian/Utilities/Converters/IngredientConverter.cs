using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters;

public sealed class IngredientConverter : JsonConverter<Ingredient>
{
    public override Ingredient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var ingredient = new Ingredient();
        var element = document.RootElement;

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in element.EnumerateArray())
                Add(entry);
        }
        else
        {
            Add(element);
        }

        return ingredient;

        void Add(JsonElement entry)
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                var identifier = entry.GetString()!;
                if (identifier.StartsWith('#'))
                    AddTag(identifier[1..]);
                else
                    ingredient.Add(new ItemStack(ItemsRegistry.Get(identifier), 1));

                return;
            }

            var raw = entry.Deserialize<RawRecipeItem>(options);
            if (!string.IsNullOrEmpty(raw.Id))
            {
                var item = ItemsRegistry.Get(raw.Id);
                ingredient.Add(new ItemStack(item, raw.Count > 0 ? raw.Count : 1));
            }
            else if (!string.IsNullOrEmpty(raw.Tag))
            {
                AddTag(raw.Tag.TrimStart('#'));
            }
            else
            {
                throw new JsonException("An ingredient must name an item or an item tag.");
            }
        }

        void AddTag(string identifier)
        {
            if (!identifier.Contains(':'))
                identifier = "minecraft:" + identifier;

            if (!TagsRegistry.TryGet("item", identifier, out var tag))
                throw new JsonException($"Unknown item tag #{identifier} in recipe ingredient.");

            // Vanilla's HolderSets follow tag reloads. File recipes are read again after tags change, before
            // publishing an immutable RecipeSet, so matching, input caches and client displays change together.
            foreach (var id in tag.Entries)
                ingredient.Add(new ItemStack(ItemsRegistry.Get(id), 1));
        }
    }

    public override void Write(Utf8JsonWriter writer, Ingredient value, JsonSerializerOptions options) =>
        throw new NotImplementedException();

    private readonly struct RawRecipeItem
    {
        public string? Id { get; init; }

        public string? Tag { get; init; }

        public int Count { get; init; }
    }
}
