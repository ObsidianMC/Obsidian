using Obsidian.Crafting;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public class DeclareRecipes : IPacket
    {
        [Field(0)]
        public int RecipesLength { get; set; }

        [Field(1, Type = DataType.Array)]
        public Dictionary<string, object> Recipes { get; set; }

        public int Id => 0x5A;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(this.Recipes.Count);

            foreach (var (name, recipe) in this.Recipes)
                await stream.WriteRecipeAsync(name, recipe);
        }
    }
}
