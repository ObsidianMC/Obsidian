using Obsidian.API.Crafting;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Registry;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class DeclareRecipes : IClientboundPacket
{
    [Field(0)]
    public IDictionary<string, IRecipe> Recipes { get; }

    public int Id => 0x65;

    public static readonly DeclareRecipes FromRegistry = new(Registry.Recipes);

    public DeclareRecipes(IDictionary<string, IRecipe> recipes)
    {
        Recipes = recipes;
    }
}
