using Obsidian.API.Crafting;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateRecipesPacket
{
    [Field(0)]
    public IDictionary<string, IRecipe> Recipes { get; }

    public static readonly UpdateRecipesPacket FromRegistry = new(RecipesRegistry.Recipes);

    public UpdateRecipesPacket(IDictionary<string, IRecipe> recipes)
    {
        Recipes = recipes;
    }
}
