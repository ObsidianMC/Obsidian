using Obsidian.API.Crafting;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateRecipesPacket(IReadOnlyDictionary<string, IRecipe> recipes)
{
    public static UpdateRecipesPacket FromRegistry => new(RecipesRegistry.Recipes);

    public IReadOnlyDictionary<string, IRecipe> Recipes { get; } = recipes;

    //TODO the whole structure
    public override void Serialize(INetStreamWriter writer) => throw new NotImplementedException();
}
