using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class WindowConfirmation : IPacket
    {
        [Field(0)]
        public sbyte WindowId { get; set; }

        [Field(1)]
        public short ActionNumber { get; set; }

        [Field(2)]
        public bool Accepted { get; set; }

        public int Id => 0x11;

        public WindowConfirmation() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.WindowId = await stream.ReadByteAsync();
            this.ActionNumber = await stream.ReadShortAsync();
            this.Accepted = await stream.ReadBooleanAsync();
        }

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
