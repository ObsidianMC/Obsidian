using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class CraftRecipeResponse : IPacket
    {
        [Field(0)]
        public sbyte WindowId { get; private set; }

        [Field(1)]
        public string RecipeId { get; private set; }

        public int Id => 0x2F;

        private CraftRecipeResponse()
        {
        }

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
