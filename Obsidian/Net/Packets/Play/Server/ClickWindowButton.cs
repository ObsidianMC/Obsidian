using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Server
{
    public partial class ClickWindowButton : IPacket
    {
        [Field(0)]
        public sbyte WindowId { get; set; }

        [Field(1)]
        public sbyte ButtonId { get; set; }

        public int Id => 0x08;

        public ClickWindowButton() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.WindowId = await stream.ReadByteAsync();
            this.ButtonId = await stream.ReadByteAsync();
        }

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
