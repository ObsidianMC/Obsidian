using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class PickItem : IServerboundPacket
    {
        [Field(0)]
        public int SlotToUse { get; private set; }

        public int Id => 0x17;

        public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
    }
}
