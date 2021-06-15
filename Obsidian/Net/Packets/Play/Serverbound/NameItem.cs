using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class NameItem : IServerboundPacket
    {
        [Field(0)]
        public string ItemName { get; set; }

        public int Id => 0x20;

        public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
    }
}
