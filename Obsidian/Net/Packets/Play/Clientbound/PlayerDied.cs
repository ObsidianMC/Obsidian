using Obsidian.Chat;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class PlayerDied : IClientboundPacket
    {
        [Field(0), VarLength]
        public int Event { get; set; } = 2;

        [Field(1), VarLength]
        public int PlayerId { get; set; }

        [Field(2)]
        public int EntityId { get; set; }

        [Field(3)]
        public ChatMessage Message { get; set; }

        public int Id => 0x31;
    }
}
