using Obsidian.Serialization.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class DestroyEntities : IClientboundPacket
    {
        [Field(0), VarLength]
        public List<int> EntityIds { get; set; } = new();

        public int Id => 0x36;

        public void AddEntity(int entity) => EntityIds.Add(entity);

        public void AddEntityRange(params int[] entities) => EntityIds.AddRange(entities);
    }
}
