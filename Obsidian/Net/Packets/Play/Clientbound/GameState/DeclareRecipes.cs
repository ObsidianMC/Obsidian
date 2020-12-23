using Obsidian.API.Crafting;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class DeclareRecipes : IPacket
    {
        [Field(0)]
        public Dictionary<string, IRecipe> Recipes { get; set; }

        public int Id => 0x5A;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(this.Recipes.Count);

            foreach (var (name, recipe) in this.Recipes)
                await stream.WriteRecipeAsync(name, recipe);
        }
    }
}
