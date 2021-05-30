using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class CraftRecipeResponse : IClientboundPacket
    {
        [Field(0)]
        public sbyte WindowId { get; }

        [Field(1)]
        public string RecipeId { get; }

        public int Id => 0x2F;

        public CraftRecipeResponse(sbyte windowId, string recipeId)
        {
            WindowId = windowId;
            RecipeId = recipeId;
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
