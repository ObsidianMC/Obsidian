using Obsidian.Boss;
using Obsidian.Serializer.Attributes;
using System;

namespace Obsidian.Net.Packets.Play
{
    public class BossBar : Packet
    {
        public BossBar() : base(0x0C) { }
        public BossBar(Guid uuid, BossBarAction action) : base(0x0C)
        {
            this.UUID = uuid;
            this.Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        [PacketOrder(0)]
        public Guid UUID { get; private set; }

        [PacketOrder(1)]
        public BossBarAction Action { get; private set; }
    }
}