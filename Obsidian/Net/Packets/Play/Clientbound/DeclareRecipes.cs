using Obsidian.API.Crafting;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Registry;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class DeclareRecipes : IClientboundPacket
    {
        [Field(0)]
        public IDictionary<string, IRecipe> Recipes { get; }

        public int Id => 0x5A;

        public static readonly DeclareRecipes FromRegistry = new(Registry.Recipes);

        public DeclareRecipes(IDictionary<string, IRecipe> recipes)
        {
            Recipes = recipes;
        }
    }
}
