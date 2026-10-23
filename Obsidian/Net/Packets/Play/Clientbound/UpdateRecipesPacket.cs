using Obsidian.API.Crafting;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>Client input filters and stonecutter choices for one immutable recipe set.</summary>
public partial class UpdateRecipesPacket
{
    private readonly IReadOnlyDictionary<string, int[]> properties;
    private readonly CuttingRecipe[] stonecutter;

    public static UpdateRecipesPacket FromRegistry => RecipesRegistry.Current.UpdatePacket;

    public UpdateRecipesPacket(IReadOnlyDictionary<string, IRecipe> recipes)
    {
        this.Recipes = recipes;
        var sets = new Dictionary<string, HashSet<int>>();
        foreach (var name in new[]
        {
            "smithing_base", "smithing_template", "smithing_addition", "furnace_input",
            "blast_furnace_input", "smoker_input", "campfire_input"
        })
        {
            sets.Add("minecraft:" + name, []);
        }

        foreach (var recipe in recipes.Values)
        {
            if (recipe is SmeltingRecipe cooking)
            {
                var name = cooking.Type switch
                {
                    CraftingType.Smelting => "furnace_input",
                    CraftingType.Blasting => "blast_furnace_input",
                    CraftingType.Smoking => "smoker_input",
                    CraftingType.CampfireCooking => "campfire_input",
                    _ => null
                };
                if (name is not null)
                    Add(name, cooking.Ingredient);
            }
            else if (recipe is SmithingTransformRecipe transform)
            {
                Add("smithing_base", transform.Base);
                Add("smithing_template", transform.Template);
                Add("smithing_addition", transform.Addition);
            }
            else if (recipe is SmithingTrimRecipe trim)
            {
                Add("smithing_base", trim.Base);
                Add("smithing_template", trim.Template);
                Add("smithing_addition", trim.Addition);
            }
        }

        this.properties = sets.ToDictionary(pair => pair.Key, pair => pair.Value.Order().ToArray());
        // RecipeManager loads identifiers lexically; this is the client's stonecutter selection order.
        this.stonecutter = recipes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .OfType<CuttingRecipe>()
            .ToArray();

        void Add(string name, Ingredient ingredient) =>
            sets["minecraft:" + name].UnionWith(ingredient.Select(item => (int)item.Holder.Id));
    }

    public IReadOnlyDictionary<string, IRecipe> Recipes { get; }

    public override void Serialize(INetStreamWriter writer)
    {
        // 1.21.11 ClientboundUpdateRecipesPacket: map<ResourceKey, list<item holder>>, then SingleInputSet.
        // Property sets use list lengths; ingredients use direct holder-set lengths (count + 1).
        writer.WriteVarInt(this.properties.Count);
        foreach (var (name, items) in this.properties)
        {
            writer.WriteString(name);
            writer.WriteVarInt(items.Length);
            foreach (var id in items)
                writer.WriteRegistryId("minecraft:item", id);
        }

        writer.WriteVarInt(this.stonecutter.Length);
        foreach (var recipe in this.stonecutter)
        {
            var items = recipe.Ingredient.Select(item => item.Holder.Id).Distinct().ToArray();
            writer.WriteVarInt(items.Length + 1);
            foreach (var id in items)
                writer.WriteRegistryId("minecraft:item", id);

            // SelectableRecipe sends only its result SlotDisplay, never a recipe identifier.
            writer.WriteVarInt(3);
            writer.WriteItemStack(recipe.Result.First());
        }
    }
}
