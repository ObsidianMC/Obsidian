using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class WindowConfirmation : IClientboundPacket, IServerboundPacket
    {
        [Field(0)]
        public sbyte WindowId { get; set; }

        [Field(1)]
        public short ActionNumber { get; set; }

        [Field(2)]
        public bool Accepted { get; set; }

        public int Id => 0x11;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
