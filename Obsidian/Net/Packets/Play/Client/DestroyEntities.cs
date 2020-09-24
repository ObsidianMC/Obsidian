using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Client
{
    public class DestroyEntities : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int Count { get; set; }

        [Field(1, Type = DataType.Array)]
        public List<int> EntityIds { get; set; } = new List<int>();

        public DestroyEntities() : base(0x38) { }

        public void AddEntity(int entity) => this.EntityIds.Add(entity);

        public void AddEntityRange(params int[] entities) => this.EntityIds.AddRange(entities);
    }
}
