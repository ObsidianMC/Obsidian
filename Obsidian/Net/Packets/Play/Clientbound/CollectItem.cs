using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class CollectItem : IClientboundPacket
    {
        [Field(0), VarLength]
        public int CollectedEntityId { get; set; }

        [Field(1), VarLength]
        public int CollectorEntityId { get; set; }

        [Field(2), VarLength]
        public int PickupItemCount { get; set; }

        public int Id => 0x55;
    }
}
