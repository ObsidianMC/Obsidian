﻿using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class EntityHeadLook : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; init; }

        [Field(1)]
        public Angle HeadYaw { get; init; }

        public int Id => 0x3A;
    }
}
