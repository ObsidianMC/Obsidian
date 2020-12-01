using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Server
{
    public class CraftRecipeRequest : IPacket
    {
        [Field(0, Type = DataType.Byte)]
        public byte WindowId { get; set; }

        [Field(1, Type = DataType.String)]
        public string RecipeId { get; set; }

        [Field(2, Type = DataType.Boolean)]
        public bool MakeAll { get; set; }

        public int Id => 0x19;

        public CraftRecipeRequest() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.WindowId = (byte) await stream.ReadByteAsync();
            this.RecipeId = await stream.ReadStringAsync();
            this.MakeAll = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            await player.client.QueuePacketAsync(new CraftRecipeResponse(WindowId, RecipeId));
        }
    }
}
