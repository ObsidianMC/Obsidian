using Obsidian.API.Crafting;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateRecipesPacket(IDictionary<string, IRecipe> recipes)
{
    [Field(0)]
    public IDictionary<string, IRecipe> Recipes { get; } = recipes;

    public static readonly UpdateRecipesPacket FromRegistry = new(RecipesRegistry.Recipes);

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Recipes.Count);
        foreach (var (name, recipe) in Recipes)
            writer.WriteRecipe(name, recipe);
    }
}
