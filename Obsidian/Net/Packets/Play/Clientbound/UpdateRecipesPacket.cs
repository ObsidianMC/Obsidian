using Obsidian.API.Crafting;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateRecipesPacket : IClientboundPacket
{
    [Field(0)]
    public IDictionary<string, IRecipe> Recipes { get; }

    public int Id => 0x77;

    public static readonly UpdateRecipesPacket FromRegistry = new(RecipesRegistry.Recipes);

    public UpdateRecipesPacket(IDictionary<string, IRecipe> recipes)
    {
        Recipes = recipes;
    }
}
