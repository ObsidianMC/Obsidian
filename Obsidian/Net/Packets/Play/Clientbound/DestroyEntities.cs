using Obsidian.Serialization.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class DestroyEntities : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; private set; } = new();

        public int Id => 0x3A;

        public DestroyEntities(int EntityId)
        {
            this.EntityId = EntityId;
        }

        public void SetEntity(int entity) => EntityId = entity;
    }
}
