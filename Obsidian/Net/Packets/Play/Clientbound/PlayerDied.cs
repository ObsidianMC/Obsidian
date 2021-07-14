﻿using Obsidian.Chat;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class PlayerDied : IClientboundPacket
    {
        [Field(0), VarLength]
        public int Event { get; init; } = 2;

        [Field(1), VarLength]
        public int PlayerId { get; init; }

        [Field(2)]
        public int EntityId { get; init; }

        [Field(3)]
        public ChatMessage Message { get; init; }

        public int Id => 0x31;
    }
}
