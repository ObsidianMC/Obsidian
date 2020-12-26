using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class CraftRecipeResponse : IPacket
    {
        [Field(0)]
        public sbyte WindowId { get; }

        [Field(1, Type = DataType.Identifier)]
        public string RecipeId { get; }

        public int Id => 0x2F;

        public CraftRecipeResponse(sbyte windowId, string recipeId)
        {
            this.WindowId = windowId;
            this.RecipeId = recipeId;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
