using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class CraftRecipeRequest : IPacket
    {
        [Field(0)]
        public sbyte WindowId { get; set; }

        [Field(1)]
        public string RecipeId { get; set; }

        [Field(2)]
        public bool MakeAll { get; set; }

        public int Id => 0x19;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.WindowId = await stream.ReadByteAsync();
            this.RecipeId = await stream.ReadStringAsync();
            this.MakeAll = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            await player.client.QueuePacketAsync(new CraftRecipeResponse(WindowId, RecipeId));
        }
    }
}
