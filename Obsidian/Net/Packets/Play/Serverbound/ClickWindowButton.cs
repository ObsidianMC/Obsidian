using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class ClickWindowButton : IServerboundPacket
    {
        [Field(0)]
        public sbyte WindowId { get; set; }

        [Field(1)]
        public sbyte ButtonId { get; set; }

        public int Id => 0x08;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
