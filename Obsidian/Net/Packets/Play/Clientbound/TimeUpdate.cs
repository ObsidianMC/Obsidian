﻿using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class TimeUpdate : IClientboundPacket
    {
        [Field(0)]
        public long WorldAge { get; }

        [Field(1)]
        public long TimeOfDay { get; }

        public int Id => 0x4E;

        public TimeUpdate(long worldAge, long timeOfDay)
        {
            WorldAge = worldAge;
            TimeOfDay = timeOfDay;
        }
    }
}
