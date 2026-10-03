using Obsidian.API.Crafting;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateRecipesPacket(IDictionary<string, IRecipe> recipes)
{
    public static readonly UpdateRecipesPacket FromRegistry = new(RecipesRegistry.Recipes);

    public IDictionary<string, IRecipe> Recipes { get; } = recipes;


    //TODO the whole structure
    public override void Serialize(INetStreamWriter writer) => throw new NotImplementedException();
}
