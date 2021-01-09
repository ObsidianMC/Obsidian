using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class SetDisplayedRecipe : IPacket
    {
        [Field(0)]
        public string RecipeId { get; set; }

        public int Id => 0x1E;

        public SetDisplayedRecipe()
        {
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.RecipeId = await stream.ReadStringAsync();
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
