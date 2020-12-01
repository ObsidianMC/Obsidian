using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public class CraftRecipeResponse : IPacket
    {
        [Field(0, Type = DataType.Byte)]
        public byte WindowId { get; }

        [Field(1, Type = DataType.Identifier)]
        public string RecipeId { get; }

        public int Id => 0x2F;

        public CraftRecipeResponse(byte windowId, string recipeId)
        {
            this.WindowId = windowId;
            this.RecipeId = recipeId;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
