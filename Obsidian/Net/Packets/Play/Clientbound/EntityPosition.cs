﻿using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class EntityPosition : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; init; }

        [Field(1), DataFormat(typeof(short))]
        public Vector Delta { get; init; }

        [Field(4)]
        public bool OnGround { get; init; }

        public int Id => 0x27;
    }
}
