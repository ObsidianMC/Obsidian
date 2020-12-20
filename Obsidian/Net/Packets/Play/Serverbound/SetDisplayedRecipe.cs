using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public class SetDisplayedRecipe : IPacket
    {
        [Field(0, Type = DataType.Identifier)]
        public string RecipeId { get; set; }

        public int Id => 0x1E;

        public SetDisplayedRecipe() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.RecipeId = await stream.ReadStringAsync();
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
